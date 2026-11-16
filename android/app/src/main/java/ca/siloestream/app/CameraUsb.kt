package ca.siloestream.app

import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.hardware.usb.UsbDevice
import android.hardware.usb.UsbDeviceConnection
import android.hardware.usb.UsbInterface
import android.hardware.usb.UsbManager
import android.os.Build
import android.os.SystemClock
import kotlinx.coroutines.flow.MutableStateFlow
import java.nio.ByteBuffer

/** Le pilote UVC natif (uvc.c) : transferts USB et conversions de couleurs. */
internal object UvcNatif {
    const val YUYV = 1
    const val NV12 = 2
    const val RGBA = 3
    const val MJPEG = 4

    /** Matrices de couleurs vers le YUV plein écart BT.601 que lit le PC (voir uvc.c). */
    const val COPIE = 0
    const val HDMI_709 = 1

    val charge: Boolean = try {
        System.loadLibrary("siloeuvc")
        true
    } catch (e: Throwable) {
        false
    }

    @JvmStatic external fun demarrer(fd: Int, point: Int, iso: Boolean, paquet: Int, transfert: Int, maxImage: Int, mjpeg: Boolean, attendu: Int): Long
    @JvmStatic external fun attendre(h: Long, dst: ByteBuffer, ms: Int, infos: LongArray?): Int
    @JvmStatic external fun arreter(h: Long)
    @JvmStatic external fun versPlans(
        format: Int, src: ByteBuffer, n: Int, w: Int, h: Int, x0: Int, y0: Int, cw: Int, ch: Int,
        y: ByteBuffer, yLigne: Int, u: ByteBuffer, uLigne: Int, uPas: Int, v: ByteBuffer, vLigne: Int, vPas: Int, matrice: Int,
    ): Int
    @JvmStatic external fun versNv21(format: Int, src: ByteBuffer, n: Int, w: Int, h: Int, dst: ByteArray, matrice: Int): Int
    @JvmStatic external fun apercu(format: Int, src: ByteBuffer, n: Int, w: Int, h: Int, dst: ByteBuffer, pw: Int, ph: Int, matrice: Int): Int
}

/**
 * Caméra HDMI (Siloé Stream 1.9.5) : une vraie caméra (Sony α6100…) branchée au téléphone par une carte de capture HDMI → USB
 * (Elgato Cam Link 4K, cartes « USB 3.0 HDMI »…) et un adaptateur USB-C. Le téléphone devient le pont sans fil de MyCam :
 * HDMI → USB → téléphone → Wi-Fi → Siloé Stream, avec le même encodeur H.264 et la même connexion que la caméra du téléphone.
 *
 * La carte est une caméra UVC : on la lit directement (UsbManager + pilote natif usbfs), sans dépendre de la prise en charge
 * des webcams USB du fabricant du téléphone. Formats : MJPEG, YUY2 et NV12, en isochrone ou en bulk, USB 2 ou USB 3.
 */
object CameraUsb {
    private const val ACTION_PERMISSION = "ca.siloestream.app.USB_PERMISSION"

    /** Une image de la carte : pixels (ou JPEG) dans [tampon], de [longueur] octets ; [tempsNs] sur SystemClock.elapsedRealtimeNanos. */
    class Image(val format: Int, val tampon: ByteBuffer, val longueur: Int, val largeur: Int, val hauteur: Int, val tempsNs: Long)

    /** « Cam Link 4K » (vide sans carte). */
    val nomCarte = MutableStateFlow("")
    /** « 1920 × 1080 · 30 i/s · YUY2 · USB 3 » une fois la vidéo reçue. */
    val mode = MutableStateFlow("")
    /** Aperçu réduit pour le viseur du téléphone (quelques images par seconde). */
    val apercu = MutableStateFlow<Bitmap?>(null)
    /** La vidéo arrive. */
    val enMarche = MutableStateFlow(false)
    /** Message à montrer quand la vidéo n'arrive pas (vide sinon). */
    val etat = MutableStateFlow("")

    private class Cadre(val format: Int, val formatIndex: Int, val frameIndex: Int, val largeur: Int, val hauteur: Int, val intervalles: List<Int>, val maxTaille: Int) {
        val nomFormat get() = when (format) {
            UvcNatif.MJPEG -> "MJPEG"
            UvcNatif.NV12 -> "NV12"
            else -> "YUY2"
        }
    }

    private class Alternative(val alt: Int, val point: Int, var bandePassante: Int)

    private class Description(
        val interfaceVs: Int,
        val cadres: List<Cadre>,
        val alternatives: List<Alternative>,
        val pointBulk: Int,
        val uvc: Int,
        val usb3: Boolean,
    )

    private class Choix(val cadre: Cadre, val intervalle: Int)

    @Volatile private var actif = false
    private var fil: Thread? = null
    private var demandee = ""

    /** Démarre la lecture de la carte (rien si elle tourne déjà). [recevoir] est appelée sur le fil USB, image par image. */
    fun demarrer(contexte: Context, hauteurVoulue: Int, fps: Int, recevoir: (Image) -> Unit) {
        if (actif && fil?.isAlive == true) return
        if (!UvcNatif.charge) {
            etat.value = "Pilote USB absent de cette version de l’application."
            return
        }
        actif = true
        val ctx = contexte.applicationContext
        fil = Thread({ boucle(ctx, hauteurVoulue, fps.coerceIn(5, 60), recevoir) }, "MyCam-HDMI").apply {
            priority = Thread.MAX_PRIORITY
            start()
        }
    }

    fun arreter() {
        actif = false
        val f = fil
        fil = null
        if (f != null && f !== Thread.currentThread()) {
            try {
                f.join(2000)
            } catch (_: InterruptedException) {
            }
        }
        enMarche.value = false
        apercu.value = null
        mode.value = ""
    }

    /** Une carte de capture (classe vidéo USB) est branchée au téléphone. */
    fun carteBranchee(contexte: Context): Boolean {
        val um = contexte.getSystemService(Context.USB_SERVICE) as? UsbManager ?: return false
        return um.deviceList.values.any { estVideo(it) }
    }

    private fun estVideo(d: UsbDevice) = (0 until d.interfaceCount).any { d.getInterface(it).interfaceClass == 14 }

    private fun nomDe(d: UsbDevice): String =
        listOfNotNull(d.manufacturerName?.trim(), d.productName?.trim()).filter { it.isNotEmpty() }.distinct().joinToString(" ")
            .ifBlank { "Carte de capture USB" }

    // ------------------------------------------------------------ boucle : chercher la carte, la lire, recommencer si on la débranche

    private fun boucle(ctx: Context, hauteurVoulue: Int, fps: Int, recevoir: (Image) -> Unit) {
        val um = ctx.getSystemService(Context.USB_SERVICE) as? UsbManager
        if (um == null) {
            etat.value = "Ce téléphone ne gère pas l’USB hôte (OTG)."
            actif = false
            return
        }
        while (actif) {
            val d = um.deviceList.values.firstOrNull { estVideo(it) }
            if (d == null) {
                nomCarte.value = ""
                enMarche.value = false
                apercu.value = null
                demandee = ""
                etat.value = "Branchez la carte de capture HDMI au téléphone (adaptateur USB-C), puis la caméra à la carte."
                dormir(800)
                continue
            }
            nomCarte.value = nomDe(d)
            if (!um.hasPermission(d)) {
                etat.value = "Autorisez Siloé Stream à utiliser « ${nomCarte.value} » (fenêtre d’Android)."
                if (demandee != d.deviceName) {
                    demandee = d.deviceName
                    demanderPermission(ctx, um, d)
                }
                dormir(600)
                continue
            }
            val c = try {
                um.openDevice(d)
            } catch (e: Exception) {
                null
            }
            if (c == null) {
                etat.value = "Impossible d’ouvrir « ${nomCarte.value} ». Débranchez-la et rebranchez-la."
                dormir(1500)
                continue
            }
            try {
                lire(d, c, hauteurVoulue, fps, recevoir)
            } catch (e: Exception) {
                etat.value = "Carte HDMI : ${e.message ?: e.javaClass.simpleName}"
                dormir(1000)
            } finally {
                try {
                    c.close()
                } catch (_: Exception) {
                }
                enMarche.value = false
                mode.value = ""
            }
        }
    }

    private fun dormir(ms: Long) {
        val fin = SystemClock.elapsedRealtime() + ms
        while (actif && SystemClock.elapsedRealtime() < fin) {
            try {
                Thread.sleep(100)
            } catch (_: InterruptedException) {
                return
            }
        }
    }

    private fun demanderPermission(ctx: Context, um: UsbManager, d: UsbDevice) {
        try {
            val drapeaux = if (Build.VERSION.SDK_INT >= 31) PendingIntent.FLAG_MUTABLE else 0
            val pi = PendingIntent.getBroadcast(ctx, 0, Intent(ACTION_PERMISSION).setPackage(ctx.packageName), drapeaux)
            um.requestPermission(d, pi)
        } catch (e: Exception) {
            etat.value = "Autorisation USB : ${e.message ?: e.javaClass.simpleName}"
        }
    }

    // ------------------------------------------------------------ une carte ouverte

    private fun lire(d: UsbDevice, c: UsbDeviceConnection, hauteurVoulue: Int, fps: Int, recevoir: (Image) -> Unit) {
        val desc = decrire(c.rawDescriptors ?: throw IllegalStateException("descripteurs illisibles"))
            ?: throw IllegalStateException("ce n’est pas une caméra UVC reconnue")
        if (desc.cadres.isEmpty()) throw IllegalStateException("aucun format vidéo connu (MJPEG, YUY2, NV12)")
        val alt0 = interfaceDe(d, desc.interfaceVs, 0) ?: throw IllegalStateException("interface vidéo introuvable")
        if (!c.claimInterface(alt0, true)) throw IllegalStateException("la carte est utilisée par une autre application")
        try {
            val choix = candidats(desc, hauteurVoulue, fps)
            if (choix.isEmpty()) throw IllegalStateException("aucun format utilisable sur ce port USB")
            for (ch in choix) {
                if (!actif) return
                when (essayer(d, c, desc, ch, recevoir)) {
                    Fin.DEBRANCHEE, Fin.ARRET -> return
                    Fin.RIEN -> continue
                }
            }
            etat.value = "La carte ne donne pas d’image. Vérifiez que la caméra est allumée et que son câble HDMI est bien branché."
            dormir(2500)
        } finally {
            try {
                c.setInterface(alt0)
            } catch (_: Exception) {
            }
            try {
                c.releaseInterface(alt0)
            } catch (_: Exception) {
            }
        }
    }

    private enum class Fin { RIEN, DEBRANCHEE, ARRET }

    private fun interfaceDe(d: UsbDevice, num: Int, alt: Int): UsbInterface? =
        (0 until d.interfaceCount).map { d.getInterface(it) }.firstOrNull { it.id == num && it.alternateSetting == alt }

    /** Un mode : négociation UVC, flux, images. RIEN si aucune image n'arrive (on essaie le mode suivant). */
    private fun essayer(d: UsbDevice, c: UsbDeviceConnection, desc: Description, ch: Choix, recevoir: (Image) -> Unit): Fin {
        val cadre = ch.cadre
        val sonde = negocier(c, desc, cadre, ch.intervalle) ?: return Fin.RIEN
        val maxImageUvc = u32(sonde, 18)
        val transfert = u32(sonde, 22).let { if (it <= 0) 3072 else it }
        val mjpeg = cadre.format == UvcNatif.MJPEG
        val attendu = when (cadre.format) {
            UvcNatif.YUYV -> cadre.largeur * cadre.hauteur * 2
            UvcNatif.NV12 -> cadre.largeur * cadre.hauteur * 3 / 2
            else -> 0
        }
        val maxImage = maxOf(attendu, maxImageUvc, cadre.maxTaille, if (mjpeg) cadre.largeur * cadre.hauteur * 2 else 0).coerceIn(256 * 1024, 48 * 1024 * 1024) + 65536

        val iso = desc.alternatives.isNotEmpty() && desc.pointBulk == 0
        var altChoisie: UsbInterface? = null
        val point: Int
        val paquet: Int
        if (iso) {
            // la plus petite alternative qui suffit au débit négocié, sinon la plus large
            val tri = desc.alternatives.sortedBy { it.bandePassante }
            val a = tri.firstOrNull { it.bandePassante >= transfert } ?: tri.last()
            altChoisie = interfaceDe(d, desc.interfaceVs, a.alt) ?: return Fin.RIEN
            if (!c.setInterface(altChoisie)) return Fin.RIEN
            point = a.point
            paquet = a.bandePassante
        } else {
            point = desc.pointBulk
            paquet = 0
        }
        val h = UvcNatif.demarrer(c.fileDescriptor, point, iso, paquet, transfert, maxImage, mjpeg, attendu)
        if (h <= 0) {
            if (altChoisie != null) interfaceDe(d, desc.interfaceVs, 0)?.let { c.setInterface(it) }
            return Fin.RIEN
        }
        val tampon = ByteBuffer.allocateDirect(maxImage)
        val infos = LongArray(4)
        val fpsReel = if (ch.intervalle > 0) (10_000_000.0 / ch.intervalle).let { Math.round(it).toInt() } else 30
        mode.value = "${cadre.largeur} × ${cadre.hauteur} · $fpsReel i/s · ${cadre.nomFormat} · ${if (desc.usb3) "USB 3" else "USB 2"}"
        val debut = SystemClock.elapsedRealtime()
        var derniere = 0L
        var images = 0
        var dernierApercu = 0L
        var fin = Fin.ARRET
        try {
            while (actif) {
                val n = UvcNatif.attendre(h, tampon, 250, infos)
                val maintenant = SystemClock.elapsedRealtime()
                if (n < 0) {
                    fin = Fin.DEBRANCHEE
                    etat.value = "Carte HDMI débranchée."
                    break
                }
                if (n == 0) {
                    if (images == 0 && maintenant - debut > 3500) {
                        fin = Fin.RIEN
                        break
                    }
                    if (images > 0 && maintenant - derniere > 2000) {
                        enMarche.value = false
                        etat.value = "Plus d’image : la caméra est-elle éteinte ou son câble HDMI débranché ?"
                    }
                    continue
                }
                images++
                derniere = maintenant
                if (!enMarche.value) {
                    enMarche.value = true
                    etat.value = ""
                }
                tampon.position(0)
                tampon.limit(n)
                val img = Image(cadre.format, tampon, n, cadre.largeur, cadre.hauteur, if (infos[0] > 0) infos[0] else SystemClock.elapsedRealtimeNanos())
                if (maintenant - dernierApercu > 140) {
                    dernierApercu = maintenant
                    faireApercu(img)
                }
                try {
                    recevoir(img)
                } catch (e: Exception) {
                    etat.value = "Image : ${e.message ?: e.javaClass.simpleName}"
                }
                tampon.clear()
            }
        } finally {
            UvcNatif.arreter(h)
            if (altChoisie != null) interfaceDe(d, desc.interfaceVs, 0)?.let {
                try {
                    c.setInterface(it)
                } catch (_: Exception) {
                }
            }
        }
        return fin
    }

    // ------------------------------------------------------------ aperçu

    private var jpegCopie = ByteArray(0)
    private var rgbaApercu: ByteBuffer? = null

    private fun faireApercu(img: Image) {
        try {
            if (img.format == UvcNatif.MJPEG) {
                if (jpegCopie.size < img.longueur) jpegCopie = ByteArray(img.longueur)
                img.tampon.position(0)
                img.tampon.get(jpegCopie, 0, img.longueur)
                img.tampon.position(0)
                val o = BitmapFactory.Options().apply { inSampleSize = if (img.largeur >= 1280) 4 else 2 }
                apercu.value = BitmapFactory.decodeByteArray(jpegCopie, 0, img.longueur, o) ?: return
            } else {
                val pw = 384
                val ph = (pw * img.hauteur / img.largeur.coerceAtLeast(1)).coerceAtLeast(2)
                val b = rgbaApercu?.takeIf { it.capacity() >= pw * ph * 4 } ?: ByteBuffer.allocateDirect(pw * ph * 4).also { rgbaApercu = it }
                if (UvcNatif.apercu(img.format, img.tampon, img.longueur, img.largeur, img.hauteur, b, pw, ph, UvcNatif.HDMI_709) != 0) return
                b.position(0)
                val bmp = Bitmap.createBitmap(pw, ph, Bitmap.Config.ARGB_8888)
                bmp.copyPixelsFromBuffer(b)
                b.position(0)
                apercu.value = bmp
            }
        } catch (_: Exception) {
        }
    }

    // ------------------------------------------------------------ négociation UVC (probe / commit)

    private fun negocier(c: UsbDeviceConnection, desc: Description, cadre: Cadre, intervalle: Int): ByteArray? {
        val preferee = when {
            desc.uvc >= 0x0150 -> 48
            desc.uvc >= 0x0110 -> 34
            else -> 26
        }
        for (longueur in listOf(preferee, 34, 26, 48).distinct()) {
            val p = ByteArray(longueur)
            p[0] = 1 // bmHint : cadence fixe
            p[2] = cadre.formatIndex.toByte()
            p[3] = cadre.frameIndex.toByte()
            ecrire32(p, 4, intervalle)
            if (c.controlTransfer(0x21, 0x01, 0x0100, desc.interfaceVs, p, longueur, 1000) < 0) continue
            val g = ByteArray(longueur)
            val lu = c.controlTransfer(0xA1, 0x81, 0x0100, desc.interfaceVs, g, longueur, 1000)
            if (lu < 26) continue
            // certaines cartes ne renvoient pas le format ou la taille : on garde ceux demandés
            if (g[2].toInt() == 0) g[2] = p[2]
            if (g[3].toInt() == 0) g[3] = p[3]
            if (u32(g, 4) == 0) ecrire32(g, 4, intervalle)
            if (c.controlTransfer(0x21, 0x01, 0x0200, desc.interfaceVs, g, longueur, 1000) < 0) continue
            return g
        }
        return null
    }

    /**
     * Les modes à essayer, du meilleur au plus sûr : la taille voulue (720p ou 1080p) en 16:9, la cadence voulue,
     * en non compressé sur l'USB 3 (rien à décoder) et en MJPEG sur l'USB 2 (la bande passante n'y suffit pas pour le reste).
     */
    private fun candidats(desc: Description, hauteurVoulue: Int, fps: Int): List<Choix> {
        val ordre = if (desc.usb3) listOf(UvcNatif.NV12, UvcNatif.YUYV, UvcNatif.MJPEG) else listOf(UvcNatif.MJPEG, UvcNatif.YUYV, UvcNatif.NV12)
        val voulu = 10_000_000 / fps
        // octets par seconde que le port supporte (isochrone USB 2 : 3 × 1024 octets × 8000 micro-trames)
        val limite = when {
            desc.usb3 -> 350_000_000L
            desc.pointBulk != 0 -> 38_000_000L
            else -> 24_000_000L
        }
        val sortie = ArrayList<Choix>()
        for (format in ordre) {
            val cadres = desc.cadres.filter { it.format == format }
            if (cadres.isEmpty()) continue
            // les tailles 16:9 d'abord, la plus proche de la hauteur voulue (sans la dépasser), puis les autres
            val tries = cadres.sortedWith(
                compareBy<Cadre>(
                    { if (it.largeur * 9 == it.hauteur * 16) 0 else 1 },
                    { if (it.hauteur <= hauteurVoulue) hauteurVoulue - it.hauteur else 10_000 + it.hauteur - hauteurVoulue },
                )
            )
            var pris = 0
            for (cadre in tries) {
                val intervalle = cadre.intervalles.filter { it in 1..voulu }.maxOrNull() ?: cadre.intervalles.filter { it > 0 }.minOrNull() ?: voulu
                if (format != UvcNatif.MJPEG) {
                    val octets = cadre.largeur.toLong() * cadre.hauteur * (if (format == UvcNatif.NV12) 3 else 4) / 2 * (10_000_000L / intervalle.coerceAtLeast(1))
                    if (octets > limite) continue
                }
                sortie.add(Choix(cadre, intervalle))
                if (++pris >= 2) break
            }
        }
        return sortie.take(5)
    }

    // ------------------------------------------------------------ descripteurs USB

    private fun u16(b: ByteArray, i: Int) = if (i + 1 < b.size) (b[i].toInt() and 0xFF) or ((b[i + 1].toInt() and 0xFF) shl 8) else 0
    private fun u32(b: ByteArray, i: Int) = if (i + 3 < b.size) u16(b, i) or (u16(b, i + 2) shl 16) else 0
    private fun ecrire32(b: ByteArray, i: Int, v: Int) {
        b[i] = v.toByte()
        b[i + 1] = (v shr 8).toByte()
        b[i + 2] = (v shr 16).toByte()
        b[i + 3] = (v shr 24).toByte()
    }

    /** Lit les descripteurs bruts : la première interface VideoStreaming, ses formats, ses tailles et ses points d'accès. */
    private fun decrire(raw: ByteArray): Description? {
        var i = 0
        var classe = 0
        var sousClasse = 0
        var numero = -1
        var alt = 0
        var uvc = 0x0100
        var usb3 = false
        var vs = -1
        var formatCourant = 0
        var indexFormat = 0
        var pointBulk = 0
        var derniereIso: Alternative? = null
        val cadres = ArrayList<Cadre>()
        val alternatives = ArrayList<Alternative>()
        while (i + 1 < raw.size) {
            val longueur = raw[i].toInt() and 0xFF
            val type = raw[i + 1].toInt() and 0xFF
            if (longueur < 2 || i + longueur > raw.size) break
            when (type) {
                4 -> if (longueur >= 9) {
                    numero = raw[i + 2].toInt() and 0xFF
                    alt = raw[i + 3].toInt() and 0xFF
                    classe = raw[i + 5].toInt() and 0xFF
                    sousClasse = raw[i + 6].toInt() and 0xFF
                    derniereIso = null
                    if (classe == 14 && sousClasse == 2 && vs < 0) vs = numero
                }
                0x24 -> if (classe == 14 && longueur >= 3) {
                    val sous = raw[i + 2].toInt() and 0xFF
                    if (sousClasse == 1 && sous == 1 && longueur >= 5) uvc = u16(raw, i + 3)
                    if (sousClasse == 2 && numero == vs) when (sous) {
                        0x04 -> if (longueur >= 21) {
                            indexFormat = raw[i + 3].toInt() and 0xFF
                            val fourcc = String(raw, i + 5, 4, Charsets.US_ASCII)
                            formatCourant = when (fourcc) {
                                "YUY2", "YUYV" -> UvcNatif.YUYV
                                "NV12" -> UvcNatif.NV12
                                else -> 0
                            }
                        }
                        0x06 -> if (longueur >= 11) {
                            indexFormat = raw[i + 3].toInt() and 0xFF
                            formatCourant = UvcNatif.MJPEG
                        }
                        0x05, 0x07 -> if (formatCourant != 0 && longueur >= 26) {
                            val nType = raw[i + 25].toInt() and 0xFF
                            val intervalles = ArrayList<Int>()
                            if (nType == 0 && longueur >= 38) {
                                val min = u32(raw, i + 26)
                                val max = u32(raw, i + 30)
                                intervalles.add(min)
                                for (f in listOf(60, 50, 30, 25, 24, 15)) {
                                    val v = 10_000_000 / f
                                    if (v in min..max) intervalles.add(v)
                                }
                                intervalles.add(max)
                            } else {
                                for (k in 0 until nType) if (26 + 4 * k + 3 < longueur) intervalles.add(u32(raw, i + 26 + 4 * k))
                            }
                            if (intervalles.isEmpty()) intervalles.add(u32(raw, i + 21))
                            cadres.add(
                                Cadre(
                                    formatCourant, indexFormat, raw[i + 3].toInt() and 0xFF,
                                    u16(raw, i + 5), u16(raw, i + 7), intervalles.filter { it > 0 }.distinct(), u32(raw, i + 17),
                                )
                            )
                        }
                        0x10, 0x11, 0x12, 0x13 -> formatCourant = 0 // formats « frame based » (H.264…) : pas pris en charge
                    }
                }
                5 -> if (classe == 14 && sousClasse == 2 && numero == vs && longueur >= 7) {
                    val adresse = raw[i + 2].toInt() and 0xFF
                    val attributs = raw[i + 3].toInt() and 3
                    val mps = u16(raw, i + 4)
                    if (adresse and 0x80 != 0) {
                        if (attributs == 1) {
                            val a = Alternative(alt, adresse, (mps and 0x7FF) * (1 + ((mps shr 11) and 3)))
                            alternatives.add(a)
                            derniereIso = a
                        } else if (attributs == 2 && alt == 0) pointBulk = adresse
                    }
                }
                0x30 -> {
                    usb3 = true
                    // USB 3 : la vraie bande passante d'un point isochrone est dans le compagnon SuperSpeed
                    val a = derniereIso
                    if (a != null && longueur >= 6) {
                        val parIntervalle = u16(raw, i + 4)
                        if (parIntervalle > 0) a.bandePassante = parIntervalle
                        derniereIso = null
                    }
                }
            }
            i += longueur
        }
        if (vs < 0) return null
        return Description(vs, cadres, alternatives.filter { it.bandePassante > 0 }, pointBulk, uvc, usb3)
    }
}
