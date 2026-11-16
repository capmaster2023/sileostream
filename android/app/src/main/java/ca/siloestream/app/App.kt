package ca.siloestream.app

import android.app.Activity
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.interaction.MutableInteractionSource
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.WindowInsetsSides
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.ime
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.only
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawing
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.SideEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.stateDescription
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.core.view.WindowCompat
import kotlinx.coroutines.flow.MutableStateFlow

/** État de l'app partagé avec l'activité. */
object AppEtat {
    val garderAllume = MutableStateFlow(true)
    /** Onglet affiché (0 Régie, 1 Bible, 2 Chants, 3 Culte, 4 MyCam). */
    val onglet = MutableStateFlow(0)
    /** Recherche à ouvrir dans l'onglet Chants (« chant 154 » tapé dans la Régie). */
    val chercherChant = MutableStateFlow("")
}

/** Siloé Stream 1.7.1 : les onglets de chaque rôle (0 Régie, 1 Bible, 2 Chants, 3 Culte, 4 MyCam). 1.9.3 : Affichage = Régie et Culte. */
fun ongletsDuRole(role: String): List<Int> = when (role) {
    "bible" -> listOf(0, 1)
    "chants" -> listOf(0, 2)
    "camera" -> listOf(4)
    "affichage" -> listOf(0, 3)
    else -> listOf(0, 1, 2, 3, 4)
}

private val ONGLETS: List<Pair<String, () -> ImageVector>> = listOf(
    "Régie" to { I.regie }, "Bible" to { I.bible }, "Chants" to { I.chants }, "Culte" to { I.culte }, "MyCam" to { I.cam },
)

@Composable
fun AppRacine(remote: Remote) {
    val link by remote.link.collectAsState()
    if (link == Link.NOT_PAIRED || link == Link.PAIRING) {
        BarresSysteme(sombre = false)
        Jumelage(remote, link == Link.PAIRING)
    } else Principal(remote)
}

/** Icônes de la barre d'état claires (MyCam) ou foncées (télécommande). */
@Composable
fun BarresSysteme(sombre: Boolean) {
    val view = LocalView.current
    SideEffect {
        val w = (view.context as? Activity)?.window ?: return@SideEffect
        val c = WindowCompat.getInsetsController(w, view)
        c.isAppearanceLightStatusBars = !sombre
        c.isAppearanceLightNavigationBars = !sombre
    }
}

@Composable
private fun Principal(remote: Remote) {
    val onglet by AppEtat.onglet.collectAsState()
    val role by remote.role.collectAsState()
    // 1.7.1 : un onglet que le rôle ne permet pas : on va au premier permis
    LaunchedEffect(role, onglet) {
        val permis = ongletsDuRole(role)
        if (onglet !in permis) AppEtat.onglet.value = permis.first()
    }
    val plein by MyCam.pleinEcran.collectAsState()
    val temoin by remote.temoin.collectAsState()
    val diffuse by MyCam.diffuse.collectAsState()
    var reglages by rememberSaveable { mutableStateOf(false) }
    val sombre = onglet == 4 || plein
    BarresSysteme(sombre)
    if (plein) {
        MyCamPleinEcran(remote)
        return
    }
    val clavier = WindowInsets.ime.getBottom(LocalDensity.current) > 0
    Box(Modifier.fillMaxSize().background(if (sombre) C.CamFond else C.Page)) {
        Column(Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing.only(WindowInsetsSides.Top + WindowInsetsSides.Horizontal))) {
            BandeauMiseAJour(remote)
            BandeauSiloeFerme(remote)
            Box(Modifier.weight(1f).fillMaxWidth().then(if (clavier) Modifier.imePadding() else Modifier)) {
                when (onglet) {
                    0 -> RegieOnglet(remote) { reglages = true }
                    1 -> BibleOnglet(remote)
                    2 -> ChantsOnglet(remote)
                    3 -> CulteOnglet(remote)
                    else -> MyCamOnglet(remote)
                }
            }
            if (!clavier && ongletsDuRole(role).size > 1) BarreOnglets(onglet, sombre, diffuse && temoin == "live", ongletsDuRole(role)) { AppEtat.onglet.value = it }
        }
        BulleHote(Modifier.align(Alignment.BottomCenter).padding(bottom = 100.dp), sombre)
        if (reglages) Reglages(remote) { reglages = false }
    }
}

@Composable
private fun BarreOnglets(actif: Int, sombre: Boolean, camLive: Boolean, permis: List<Int>, choisir: (Int) -> Unit) {
    val fond = if (sombre) C.CamBarre else C.Carte
    Column(Modifier.fillMaxWidth().background(fond)) {
        Box(Modifier.fillMaxWidth().height(1.dp).background(if (sombre) Color(0xFF1F2940) else C.Trait))
        Row(Modifier.fillMaxWidth().navigationBarsPadding().padding(start = 4.dp, end = 4.dp, top = 6.dp, bottom = 8.dp)) {
            ONGLETS.forEachIndexed { i, (nom, icone) ->
                if (i !in permis) return@forEachIndexed
                val on = i == actif
                val couleur = when {
                    on && sombre -> C.OrClair
                    on -> C.Or
                    sombre -> C.CamBrume
                    else -> C.Inactif
                }
                Column(
                    Modifier.weight(1f).heightIn(min = 52.dp).clip(RoundedCornerShape(12.dp))
                        .selectable(selected = on, role = Role.Tab) { choisir(i) }
                        .then(if (i == 4 && camLive) Modifier.semantics { stateDescription = "caméra à l’écran" } else Modifier),
                    horizontalAlignment = Alignment.CenterHorizontally,
                    verticalArrangement = Arrangement.spacedBy(3.dp, Alignment.CenterVertically),
                ) {
                    Box(
                        Modifier.size(width = 56.dp, height = 30.dp).clip(RoundedCornerShape(15.dp))
                            .background(if (!on) Color.Transparent else if (sombre) Color(0x29E3B762) else C.OrPale),
                        contentAlignment = Alignment.Center,
                    ) {
                        Ico(icone(), couleur, 22.dp)
                        if (i == 4 && camLive) Box(Modifier.align(Alignment.TopEnd).padding(top = 2.dp, end = 12.dp)) { Point(C.Rouge, 10.dp, anneau = fond) }
                    }
                    T(nom, taille = 11.5.sp, poids = if (on) FontWeight.ExtraBold else FontWeight.Bold, couleur = couleur, lignes = 1)
                }
            }
        }
    }
}

/** Nouvelle version de l'APK sur siloestream.tech : bandeau or. */
@Composable
private fun BandeauMiseAJour(remote: Remote) {
    val context = LocalContext.current
    val maj by remote.update.collectAsState()
    val (version, url) = maj ?: return
    Row(
        Modifier.fillMaxWidth().padding(start = 12.dp, end = 12.dp, top = 8.dp).clip(RoundedCornerShape(14.dp)).background(C.OrPale)
            .clickable(role = Role.Button) {
                try {
                    context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)))
                } catch (e: Exception) {
                    Bulles.dire("Aucun navigateur pour télécharger la mise à jour.")
                }
            }
            .padding(horizontal = 14.dp, vertical = 10.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Ico(I.telecharger, C.Or, 20.dp)
        T("Siloé Stream $version est prête : télécharger", taille = 13.5.sp, poids = FontWeight.ExtraBold, couleur = C.Or, modifier = Modifier.weight(1f), lignes = 2)
    }
}

/**
 * Siloé Stream 1.7.0 : par Internet, Siloé Stream est fermé sur le PC de l'église (l'agent répond) : un bouton le démarre ;
 * pendant le démarrage, on patiente (le téléphone se reconnecte tout seul quand il est prêt).
 */
@Composable
private fun BandeauSiloeFerme(remote: Remote) {
    val app by remote.appPc.collectAsState()
    val link by remote.link.collectAsState()
    val pc by remote.pcName.collectAsState()
    if (link != Link.CONNECTED || app !in setOf("ferme", "demarrage", "ouvert")) return
    Column(
        Modifier.fillMaxWidth().padding(start = 12.dp, end = 12.dp, top = 8.dp).clip(RoundedCornerShape(14.dp)).background(C.AmbrePale)
            .padding(horizontal = 14.dp, vertical = 12.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Ico(I.ecran, C.AmbreTexte, 20.dp)
            T(
                if (app == "ferme") "Siloé Stream est fermé sur « ${pc.ifBlank { "le PC" }} »" else "Démarrage de Siloé Stream sur le PC…",
                taille = 13.5.sp, poids = FontWeight.ExtraBold, couleur = C.AmbreTexte, modifier = Modifier.weight(1f), lignes = 2,
            )
        }
        val role by remote.role.collectAsState()
        if (app == "ferme" && role == "admin") Touche(onClick = { if (remote.demarrerSiloe()) remote.appPc.value = "demarrage" }, modifier = Modifier.fillMaxWidth(), fond = C.Ambre) {
            T("Démarrer Siloé Stream", couleur = Color.White, poids = FontWeight.ExtraBold)
        }
    }
}

/** Réglages de la télécommande (volet plein écran). */
@Composable
private fun Reglages(remote: Remote, fermer: () -> Unit) {
    val store = remote.store
    val pc by remote.pcName.collectAsState()
    val proto by remote.proto.collectAsState()
    var volume by remember { mutableStateOf(store.volumeKeys) }
    var allume by remember { mutableStateOf(store.keepScreenOn) }
    var bip by remember { mutableStateOf(store.couperBip) }
    var journal by remember { mutableStateOf(false) }
    var oublier by remember { mutableStateOf(false) }
    var fermerPc by remember { mutableStateOf(false) }
    val app by remote.appPc.collectAsState()
    val route by remote.route.collectAsState()
    val link by remote.link.collectAsState()
    val role by remote.role.collectAsState()
    val context = LocalContext.current
    val version = remember {
        try {
            context.packageManager.getPackageInfo(context.packageName, 0).versionName ?: "?"
        } catch (e: Exception) {
            "?"
        }
    }
    if (journal) JournalDialog(remote) { journal = false }
    if (fermerPc) AlertDialog(
        onDismissRequest = { fermerPc = false },
        title = { Text("Fermer Siloé Stream sur « $pc » ?", fontFamily = Manrope, fontWeight = FontWeight.ExtraBold) },
        text = { Text("L'écran de l'église et MyStream s'arrêtent. Vous pourrez le redémarrer d'ici (bouton « Démarrer Siloé Stream »).", fontFamily = Manrope) },
        confirmButton = { TextButton(onClick = { fermerPc = false; if (!remote.fermerSiloe()) Bulles.dire("PC injoignable.") }) { Text("Fermer", color = C.RougeTexte, fontFamily = Manrope, fontWeight = FontWeight.Bold) } },
        dismissButton = { TextButton(onClick = { fermerPc = false }) { Text("Annuler", color = C.Encre, fontFamily = Manrope) } },
        containerColor = C.Carte,
    )
    if (oublier) AlertDialog(
        onDismissRequest = { oublier = false },
        title = { Text("Oublier « $pc » ?", fontFamily = Manrope, fontWeight = FontWeight.ExtraBold) },
        text = { Text("Il faudra scanner de nouveau le code QR du PC pour reconnecter ce téléphone.", fontFamily = Manrope) },
        confirmButton = { TextButton(onClick = { oublier = false; fermer(); MyCam.arreterDiffusion(); remote.forget() }) { Text("Oublier", color = C.RougeTexte, fontFamily = Manrope, fontWeight = FontWeight.Bold) } },
        dismissButton = { TextButton(onClick = { oublier = false }) { Text("Annuler", color = C.Encre, fontFamily = Manrope) } },
        containerColor = C.Carte,
    )
    Box(Modifier.fillMaxSize().background(C.Page).clickable(interactionSource = remember { MutableInteractionSource() }, indication = null) {}) {
        Column(Modifier.fillMaxSize().windowInsetsPadding(WindowInsets.safeDrawing)) {
            Row(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
                Touche(onClick = fermer, modifier = Modifier.size(44.dp), fond = C.Carte, bord = C.Trait, rayon = 22.dp, description = "Fermer les réglages", padding = PaddingValues(0.dp)) {
                    Ico(I.gauche, C.Encre, 20.dp)
                }
                Spacer(Modifier.width(10.dp))
                T("Réglages", taille = 22.sp, poids = FontWeight.ExtraBold)
            }
            Column(
                Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 16.dp).padding(bottom = 24.dp),
                verticalArrangement = Arrangement.spacedBy(16.dp),
            ) {
                Carte(padding = PaddingValues(horizontal = 14.dp, vertical = 4.dp), espace = 0.dp) {
                    LigneReglage("Boutons de volume", "Volume + : Suivant · volume − : Précédent", volume) { volume = it; store.volumeKeys = it }
                    LigneReglage("Garder l’écran allumé", "Pendant le culte, le téléphone ne se met pas en veille", allume) {
                        allume = it; store.keepScreenOn = it; AppEtat.garderAllume.value = it
                    }
                    LigneReglage("Détecteur de versets sans bip", "Coupe le bip du micro quand l’écoute reprend", bip) { bip = it; store.couperBip = it }
                }
                Carte {
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        Box(Modifier.size(44.dp).clip(RoundedCornerShape(14.dp)).background(C.OrPale), contentAlignment = Alignment.Center) { Ico(I.ecran, C.Or, 22.dp) }
                        Column(Modifier.weight(1f)) {
                            T(pc.ifBlank { "PC" }, taille = 15.sp, poids = FontWeight.ExtraBold, lignes = 1)
                            T(if (proto >= 2) "Siloé Stream 4.0 ou plus" else "Siloé Stream 3.3 ou avant : mettez le PC à jour pour le déroulé, Noir et MyCam", taille = 12.5.sp, couleur = C.Encre2)
                            // 1.7.0 : par où passe la connexion
                            if (link == Link.CONNECTED) T(
                                if (route == "internet") "Relié par Internet (relais Siloé Stream)" + (if (app == "ferme") " · Siloé Stream fermé sur le PC" else "")
                                else "Relié par le Wi-Fi de l'église",
                                taille = 12.5.sp, couleur = C.Brume,
                            )
                        }
                    }
                    // 1.7.0 : démarrer / fermer Siloé Stream sur le PC, de n'importe où
                    if (link == Link.CONNECTED && app == "ferme" && role == "admin") Touche(onClick = { if (remote.demarrerSiloe()) remote.appPc.value = "demarrage" }, modifier = Modifier.fillMaxWidth(), bord = C.Trait) {
                        Ico(I.ecran, C.Or, 18.dp)
                        Spacer(Modifier.width(8.dp))
                        T("Démarrer Siloé Stream sur le PC", couleur = C.Or, poids = FontWeight.ExtraBold)
                    }
                    if (link == Link.CONNECTED && app.isEmpty() && proto >= 2 && role == "admin") Touche(onClick = { fermerPc = true }, modifier = Modifier.fillMaxWidth(), bord = C.Trait) {
                        Ico(I.sortir, C.Encre2, 18.dp)
                        Spacer(Modifier.width(8.dp))
                        T("Fermer Siloé Stream sur le PC", couleur = C.Encre2, poids = FontWeight.ExtraBold)
                    }
                    Touche(onClick = { oublier = true }, modifier = Modifier.fillMaxWidth(), bord = C.RougeBord) {
                        Ico(I.sortir, C.RougeTexte, 18.dp)
                        Spacer(Modifier.width(8.dp))
                        T("Oublier ce PC", couleur = C.RougeTexte, poids = FontWeight.ExtraBold)
                    }
                }
                // 1.9.1 : comptes Google, église et membres
                ReglagesEglise(remote)
                Touche(onClick = { journal = true }, modifier = Modifier.fillMaxWidth(), bord = C.Trait) {
                    Ico(I.journal, C.Or, 18.dp)
                    Spacer(Modifier.width(8.dp))
                    T("Journal d’erreurs", couleur = C.Or, poids = FontWeight.ExtraBold)
                }
                T("Siloé Stream $version · polices Manrope et Cormorant Garamond (SIL Open Font License)", taille = 12.sp, couleur = C.Brume, centre = true, modifier = Modifier.fillMaxWidth())
            }
        }
    }
}

/** Journal d'erreurs : connexions, adresses essayées, refus, pertes de Wi-Fi. Bouton Copier pour l'envoyer. */
@Composable
fun JournalDialog(remote: Remote, onClose: () -> Unit) {
    val context = LocalContext.current
    val lines by remote.journal.collectAsState()
    val text = remember(lines) { remote.journalText() }
    AlertDialog(
        onDismissRequest = onClose,
        title = { Text("Journal d’erreurs", fontFamily = Manrope, fontWeight = FontWeight.ExtraBold) },
        text = {
            LazyColumn(Modifier.fillMaxWidth().height(380.dp)) {
                items(text.lines()) { l ->
                    val alerte = l.contains("refusé") || l.contains("introuvable") || l.contains("erreur") || l.contains("perdu")
                    Text(l, fontFamily = FontFamily.Monospace, fontSize = 11.sp, lineHeight = 15.sp, color = if (alerte) C.RougeTexte else C.Encre)
                }
            }
        },
        confirmButton = {
            TextButton(onClick = {
                val cm = context.getSystemService(ClipboardManager::class.java)
                cm?.setPrimaryClip(ClipData.newPlainText("Journal Siloé Stream", text))
                Bulles.dire("Journal copié : collez-le dans votre message.")
            }) { Text("Copier", color = C.Or, fontFamily = Manrope, fontWeight = FontWeight.Bold) }
        },
        dismissButton = { TextButton(onClick = onClose) { Text("Fermer", color = C.Encre, fontFamily = Manrope) } },
        containerColor = C.Carte,
    )
}

/** En-tête d'onglet : surtitre, grand titre, et ce qu'on veut à droite. */
@Composable
fun EnTete(surtitre: String, titre: String, sombre: Boolean = false, droite: @Composable () -> Unit = {}) {
    Row(Modifier.fillMaxWidth().padding(horizontal = 20.dp).padding(top = 16.dp), verticalAlignment = Alignment.Bottom, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
        Column(Modifier.weight(1f)) {
            T(surtitre, taille = 12.sp, poids = FontWeight.Bold, couleur = if (sombre) C.CamBrume else C.Brume, lignes = 1)
            T(titre, taille = 28.sp, poids = FontWeight.ExtraBold, couleur = if (sombre) C.CamTexte else C.Encre, lignes = 1, espacement = (-0.4).sp)
        }
        droite()
    }
}

private class Pastille(val fond: Color, val texte: Color, val point: Color, val libelle: String)

/** Pastille de connexion au PC (vert : relié, ambre : connexion, rouge : hors ligne). */
@Composable
fun PastilleConnexion(remote: Remote) {
    val link by remote.link.collectAsState()
    val pc by remote.pcName.collectAsState()
    val app by remote.appPc.collectAsState()
    val e = when (link) {
        // 1.7.0 : par Internet, le PC répond mais Siloé Stream y est fermé
        Link.CONNECTED -> if (app.isNotEmpty() && app != "fermeture") Pastille(C.AmbrePale, C.AmbreTexte, C.Ambre, if (app == "demarrage") "Démarrage…" else "Siloé Stream fermé")
            else Pastille(C.VertFond, C.VertTexte, C.VertPoint, pc.ifBlank { "PC" })
        Link.CONNECTING -> Pastille(C.AmbrePale, C.AmbreTexte, C.Ambre, "Connexion…")
        else -> Pastille(Color(0xFFFDECEA), C.RougeTexte, C.Rouge, "Hors ligne")
    }
    Row(
        Modifier.heightIn(min = 30.dp).clip(RoundedCornerShape(15.dp)).background(e.fond).padding(horizontal = 12.dp),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(6.dp),
    ) {
        Point(e.point, 8.dp)
        T(e.libelle.uppercase(), taille = 12.sp, poids = FontWeight.ExtraBold, couleur = e.texte, lignes = 1)
    }
}
