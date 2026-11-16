package ca.siloestream.app

import android.app.Activity
import android.content.Context
import android.content.ContextWrapper
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.credentials.CredentialManager
import androidx.credentials.CustomCredential
import androidx.credentials.GetCredentialRequest
import androidx.credentials.exceptions.GetCredentialCancellationException
import androidx.credentials.exceptions.GetCredentialException
import com.google.android.libraries.identity.googleid.GetSignInWithGoogleOption
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import org.json.JSONArray
import org.json.JSONObject
import java.util.concurrent.TimeUnit

/** Client Web de Google Cloud (projet Siloé Stream) : le serveur vérifie que le jeton Google lui est destiné. */
const val GOOGLE_CLIENT_WEB = "651141034269-s9hq1jb8bafbiipoprof0v1qm2175l9d.apps.googleusercontent.com"

/**
 * Siloé Stream 1.9.1 : comptes Google et églises (API du relais). L'admin crée son église, relie le PC et ajoute ses membres
 * par leur Gmail avec un rôle ; un membre se connecte avec Google et entre au PC de son église sans code QR.
 */
object Comptes {
    class Erreur(msg: String) : Exception(msg)
    enum class Renouv { OK, RETIRE, HORS_LIGNE }

    /** Le compte connecté (réponse « moi » : email, nom, eglises), null si déconnecté. */
    val moi = MutableStateFlow<JSONObject?>(null)

    private val http = OkHttpClient.Builder().connectTimeout(10, TimeUnit.SECONDS).readTimeout(15, TimeUnit.SECONDS).build()
    private val JSON_TYPE = "application/json; charset=utf-8".toMediaType()

    private fun base(store: Store): String {
        val r = (store.pairing()?.relay ?: RELAIS_DEFAUT).trim().trimEnd('/')
        return when {
            r.startsWith("ws://") -> "http://" + r.removePrefix("ws://")
            r.startsWith("wss://") -> "https://" + r.removePrefix("wss://")
            r.startsWith("http") -> r
            else -> "https://$r"
        } + "/api/"
    }

    /** Appel bloquant (à lancer hors du fil principal). */
    private fun appel(store: Store, methode: String, chemin: String, corps: JSONObject? = null): JSONObject {
        val b = Request.Builder().url(base(store) + chemin)
        if (store.session.isNotEmpty()) b.header("Authorization", "Bearer " + store.session)
        when (methode) {
            "GET" -> b.get()
            "DELETE" -> b.delete((corps ?: JSONObject()).toString().toRequestBody(JSON_TYPE))
            else -> b.post((corps ?: JSONObject()).toString().toRequestBody(JSON_TYPE))
        }
        http.newCall(b.build()).execute().use { r ->
            val texte = r.body?.string().orEmpty()
            val j = try { JSONObject(texte) } catch (e: Exception) { JSONObject() }
            if (r.code == 401 && chemin != "connexion") {
                store.session = ""
                moi.value = null
            }
            if (!r.isSuccessful) throw Erreur(j.optString("erreur").ifBlank { "Le serveur ne répond pas comme prévu (${r.code})." })
            return j
        }
    }

    suspend fun api(store: Store, methode: String, chemin: String, corps: JSONObject? = null): JSONObject =
        withContext(Dispatchers.IO) {
            try {
                appel(store, methode, chemin, corps)
            } catch (e: java.io.IOException) {
                throw Erreur("Pas de connexion au serveur Siloé Stream. Vérifiez Internet.")
            }
        }

    /** « Se connecter avec Google » (Credential Manager), puis session auprès du serveur. */
    suspend fun connecter(activity: Activity, store: Store) {
        val option = GetSignInWithGoogleOption.Builder(GOOGLE_CLIENT_WEB).build()
        val demande = GetCredentialRequest.Builder().addCredentialOption(option).build()
        val r = try {
            CredentialManager.create(activity).getCredential(activity, demande)
        } catch (e: GetCredentialCancellationException) {
            return
        } catch (e: GetCredentialException) {
            throw Erreur("Connexion Google impossible : ${e.message ?: e.type}")
        }
        val c = r.credential
        if (c !is CustomCredential || c.type != GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL) throw Erreur("Réponse Google inattendue.")
        val jeton = GoogleIdTokenCredential.createFrom(c.data).idToken
        val j = api(store, "POST", "connexion", JSONObject().put("idToken", jeton))
        store.session = j.getString("jeton")
        moi.value = j
    }

    suspend fun rafraichir(store: Store) {
        if (store.session.isEmpty()) {
            moi.value = null
            return
        }
        try {
            moi.value = api(store, "GET", "moi")
        } catch (e: Erreur) {
            // hors ligne : on garde ce qu'on avait
        }
    }

    suspend fun deconnecter(store: Store) {
        try { api(store, "POST", "deconnexion") } catch (e: Erreur) {}
        store.session = ""
        moi.value = null
    }

    /** Nouveau billet pour l'église de ce téléphone (appelé par la boucle de reconnexion). */
    suspend fun renouveler(store: Store): Renouv {
        if (store.session.isEmpty()) return Renouv.HORS_LIGNE
        return try {
            val b = api(store, "POST", "eglises/${store.egliseId}/billet", JSONObject().put("deviceId", store.deviceId))
            store.billet = b.getString("billet")
            store.billetExp = b.optLong("exp")
            store.role = b.optString("role", store.role)
            Renouv.OK
        } catch (e: Erreur) {
            if (e.message?.contains("introuvable") == true) Renouv.RETIRE else Renouv.HORS_LIGNE
        }
    }
}

fun Context.activite(): Activity? {
    var c: Context? = this
    while (c is ContextWrapper) {
        if (c is Activity) return c
        c = c.baseContext
    }
    return null
}

private val NOMS_ROLES = listOf("admin" to "Admin", "bible" to "Versets", "chants" to "Chants", "affichage" to "Affichage", "camera" to "Caméraman")
fun nomRole(r: String) = NOMS_ROLES.firstOrNull { it.first == r }?.second ?: "Admin"

@Composable
private fun Champ(valeur: String, change: (String) -> Unit, indice: String, clavier: KeyboardType = KeyboardType.Text) {
    BasicTextField(
        value = valeur,
        onValueChange = change,
        modifier = Modifier.fillMaxWidth().heightIn(min = 48.dp).clip(RoundedCornerShape(14.dp)).background(C.Page)
            .border(1.dp, C.Trait2, RoundedCornerShape(14.dp)).padding(horizontal = 14.dp, vertical = 13.dp),
        singleLine = true,
        textStyle = TextStyle(fontFamily = Manrope, fontSize = 15.sp, fontWeight = FontWeight.Bold, color = C.Encre),
        cursorBrush = SolidColor(C.Or),
        keyboardOptions = KeyboardOptions(keyboardType = clavier),
        decorationBox = { champ ->
            Box {
                if (valeur.isEmpty()) T(indice, taille = 15.sp, couleur = C.Brume, lignes = 1)
                champ()
            }
        },
    )
}

@Composable
private fun BoutonGoogle(occupe: Boolean, onClick: () -> Unit) {
    Touche(onClick = onClick, modifier = Modifier.fillMaxWidth().heightIn(min = 52.dp), fond = Color.White, bord = C.Trait2, rayon = 16.dp, actif = !occupe) {
        if (occupe) CircularProgressIndicator(Modifier.size(18.dp), color = C.Or, strokeWidth = 2.dp)
        else Box(Modifier.size(22.dp).clip(RoundedCornerShape(11.dp)).background(Color(0xFF4285F4)), contentAlignment = Alignment.Center) {
            T("G", taille = 13.sp, poids = FontWeight.ExtraBold, couleur = Color.White)
        }
        Spacer(Modifier.width(10.dp))
        T("Se connecter avec Google", taille = 15.5.sp, poids = FontWeight.ExtraBold)
    }
}

/**
 * Écran de jumelage : entrer par son église (compte Google), ou créer son église.
 */
@Composable
fun CarteEglise(remote: Remote) {
    val store = remote.store
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val moi by Comptes.moi.collectAsState()
    var occupe by remember { mutableStateOf(false) }
    var nouvelle by remember { mutableStateOf("") }
    LaunchedEffect(Unit) { Comptes.rafraichir(store) }
    fun agir(bloc: suspend () -> Unit) {
        if (occupe) return
        occupe = true
        scope.launch {
            try { bloc() } catch (e: Comptes.Erreur) { Bulles.dire(e.message ?: "Erreur") } finally { occupe = false }
        }
    }
    Carte(rayon = 24.dp, padding = androidx.compose.foundation.layout.PaddingValues(18.dp), espace = 12.dp) {
        Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
            T("Entrer avec mon église", taille = 18.sp, poids = FontWeight.ExtraBold)
            T(
                if (moi == null) "Votre admin vous a ajouté avec votre Gmail ? Connectez-vous : pas de code QR à scanner, même de la maison."
                else "Connecté : ${moi!!.optString("email")}",
                taille = 13.5.sp, couleur = C.Encre2, interligne = 19.sp,
            )
        }
        val m = moi
        if (m == null) {
            BoutonGoogle(occupe) {
                val a = context.activite() ?: return@BoutonGoogle
                agir { Comptes.connecter(a, store) }
            }
            return@Carte
        }
        val eglises = m.optJSONArray("eglises") ?: JSONArray()
        if (eglises.length() == 0) T("Aucune église pour ce compte. Demandez à votre admin de vous ajouter, ou créez la vôtre.", taille = 13.sp, couleur = C.Brume, interligne = 18.sp)
        for (i in 0 until eglises.length()) {
            val e = eglises.getJSONObject(i)
            val pc = e.optJSONObject("pc")
            Row(
                Modifier.fillMaxWidth().clip(RoundedCornerShape(16.dp)).background(C.Page).padding(12.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Column(Modifier.weight(1f)) {
                    T(e.optString("nom"), taille = 15.sp, poids = FontWeight.ExtraBold, lignes = 1)
                    T(nomRole(e.optString("role")) + " · " + (pc?.optString("nom")?.let { "PC « $it »" } ?: "PC pas encore relié"), taille = 12.5.sp, couleur = C.Encre2, lignes = 1)
                }
                Touche(onClick = {
                    if (pc == null) {
                        Bulles.dire(if (e.optString("role") == "admin") "Scannez d'abord le code QR du PC, puis Réglages › Église › Relier ce PC." else "L'admin doit d'abord relier le PC de l'église.")
                        return@Touche
                    }
                    agir {
                        val b = Comptes.api(store, "POST", "eglises/${e.getString("id")}/billet", JSONObject().put("deviceId", store.deviceId))
                        remote.entrerEglise(e.getString("id"), b)
                    }
                }, fond = C.Marine, rayon = 12.dp, actif = !occupe) {
                    T("Entrer", couleur = Color.White, poids = FontWeight.ExtraBold)
                }
            }
        }
        Champ(nouvelle, { nouvelle = it.take(80) }, "Nom de votre église (pour la créer)")
        if (nouvelle.isNotBlank()) Touche(onClick = {
            agir {
                val r = Comptes.api(store, "POST", "eglises", JSONObject().put("nom", nouvelle.trim()))
                Comptes.moi.value = r
                nouvelle = ""
                Bulles.dire("Église créée : vous êtes admin. Scannez le code QR du PC, puis Réglages › Église › Relier ce PC.")
            }
        }, modifier = Modifier.fillMaxWidth(), bord = C.Trait2, actif = !occupe) {
            T("Créer mon église", couleur = C.Or, poids = FontWeight.ExtraBold)
        }
        Touche(onClick = { agir { Comptes.deconnecter(store) } }, modifier = Modifier.fillMaxWidth(), fond = Color.Transparent) {
            T("Se déconnecter", couleur = C.Brume, poids = FontWeight.Bold)
        }
    }
}

/**
 * Réglages › Église : le compte, relier le PC à l'église (admin jumelé par QR) et gérer les membres (Gmail + rôle).
 */
@Composable
fun ReglagesEglise(remote: Remote) {
    val store = remote.store
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val moi by Comptes.moi.collectAsState()
    val eglisePc by remote.eglisePc.collectAsState()
    val link by remote.link.collectAsState()
    val role by remote.role.collectAsState()
    var occupe by remember { mutableStateOf(false) }
    var membres by remember { mutableStateOf<JSONObject?>(null) }
    var courriel by remember { mutableStateOf("") }
    var roleNouveau by remember { mutableStateOf("bible") }
    var choixRole by remember { mutableStateOf<String?>(null) } // email du membre dont on change le rôle
    var nouvelle by remember { mutableStateOf("") }
    LaunchedEffect(Unit) { Comptes.rafraichir(store) }
    fun agir(bloc: suspend () -> Unit) {
        if (occupe) return
        occupe = true
        scope.launch {
            try { bloc() } catch (e: Comptes.Erreur) { Bulles.dire(e.message ?: "Erreur") } finally { occupe = false }
        }
    }
    val eglises = moi?.optJSONArray("eglises") ?: JSONArray()
    // l'église gérée ici : celle du PC, sinon celle où je suis admin
    val geree: JSONObject? = (0 until eglises.length()).map { eglises.getJSONObject(it) }.let { l ->
        l.firstOrNull { it.optString("id") == eglisePc } ?: l.firstOrNull { it.optString("id") == store.egliseId } ?: l.firstOrNull { it.optString("role") == "admin" }
    }
    val admin = geree?.optString("role") == "admin"
    suspend fun chargerMembres() {
        if (geree != null && admin) membres = Comptes.api(store, "GET", "eglises/${geree.getString("id")}/membres")
    }
    LaunchedEffect(geree?.optString("id"), admin) {
        try { chargerMembres() } catch (e: Comptes.Erreur) {}
    }
    LaunchedEffect(Unit) {
        remote.events.collect { e ->
            when (e.optString("t")) {
                "compte.lie" -> {
                    Bulles.dire(if (e.optString("eglise").isNotEmpty()) "PC relié à « ${e.optString("nom")} »." else "PC détaché de l'église.")
                    Comptes.rafraichir(store)
                    try { chargerMembres() } catch (x: Comptes.Erreur) {}
                }
                "error" -> if (e.optString("message").startsWith("Liaison")) Bulles.dire(e.optString("message"))
            }
        }
    }

    choixRole?.let { email ->
        AlertDialog(
            onDismissRequest = { choixRole = null },
            title = { Text(email, fontFamily = Manrope, fontWeight = FontWeight.ExtraBold, fontSize = 16.sp) },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                    NOMS_ROLES.forEach { (r, nom) ->
                        Touche(onClick = {
                            choixRole = null
                            agir {
                                Comptes.api(store, "POST", "eglises/${geree!!.getString("id")}/membres", JSONObject().put("email", email).put("role", r))
                                chargerMembres()
                            }
                        }, modifier = Modifier.fillMaxWidth(), bord = C.Trait2) { T(nom, poids = FontWeight.ExtraBold) }
                    }
                    Touche(onClick = {
                        choixRole = null
                        agir {
                            Comptes.api(store, "DELETE", "eglises/${geree!!.getString("id")}/membres/" + android.net.Uri.encode(email))
                            chargerMembres()
                        }
                    }, modifier = Modifier.fillMaxWidth(), bord = C.RougeBord) { T("Retirer de l'église", couleur = C.RougeTexte, poids = FontWeight.ExtraBold) }
                }
            },
            confirmButton = { TextButton(onClick = { choixRole = null }) { Text("Fermer", color = C.Encre, fontFamily = Manrope) } },
            containerColor = C.Carte,
        )
    }

    Carte(padding = androidx.compose.foundation.layout.PaddingValues(14.dp), espace = 10.dp) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Box(Modifier.size(44.dp).clip(RoundedCornerShape(14.dp)).background(C.OrPale), contentAlignment = Alignment.Center) { Ico(I.bouclier, C.Or, 22.dp) }
            Column(Modifier.weight(1f)) {
                T(geree?.optString("nom") ?: "Église", taille = 15.sp, poids = FontWeight.ExtraBold, lignes = 1)
                T(moi?.optString("email") ?: "Comptes Google : admin, Versets, Chants, Caméraman", taille = 12.5.sp, couleur = C.Encre2, lignes = 2)
            }
        }
        val m = moi
        if (m == null) {
            BoutonGoogle(occupe) {
                val a = context.activite() ?: return@BoutonGoogle
                agir { Comptes.connecter(a, store) }
            }
            return@Carte
        }
        if (geree == null) {
            T("Créez votre église : vous en serez l'admin, puis vous ajouterez vos membres par leur Gmail.", taille = 13.sp, couleur = C.Encre2, interligne = 18.sp)
            Champ(nouvelle, { nouvelle = it.take(80) }, "Nom de votre église")
            Touche(onClick = {
                agir {
                    Comptes.moi.value = Comptes.api(store, "POST", "eglises", JSONObject().put("nom", nouvelle.trim()))
                    nouvelle = ""
                }
            }, modifier = Modifier.fillMaxWidth(), fond = C.Marine, actif = !occupe && nouvelle.isNotBlank()) {
                T("Créer mon église", couleur = Color.White, poids = FontWeight.ExtraBold)
            }
        } else {
            T("Votre rôle : ${nomRole(geree.optString("role"))}", taille = 13.sp, couleur = C.Encre2)
            // relier le PC (admin, relié au PC en tant qu'admin)
            if (admin && link == Link.CONNECTED && role == "admin") {
                if (eglisePc != geree.optString("id")) Touche(onClick = {
                    agir {
                        val c = Comptes.api(store, "POST", "eglises/${geree.getString("id")}/code-pc")
                        if (!remote.send("compte.lier", "code" to c.getString("code"), "relais" to c.optString("relais"))) Bulles.dire("PC injoignable.")
                        else Bulles.dire("Liaison du PC en cours…")
                    }
                }, modifier = Modifier.fillMaxWidth(), fond = C.Marine, actif = !occupe) {
                    Ico(I.ecran, C.OrClair, 18.dp)
                    Spacer(Modifier.width(8.dp))
                    T("Relier ce PC à « ${geree.optString("nom")} »", couleur = Color.White, poids = FontWeight.ExtraBold, lignes = 1)
                }
                else T("✓ Ce PC est relié à l'église : vos membres y entrent avec leur compte Google.", taille = 13.sp, couleur = C.Or, interligne = 18.sp)
            } else if (admin && geree.optJSONObject("pc") == null) {
                T("Pour relier le PC : connectez ce téléphone au PC (code QR), puis revenez ici.", taille = 13.sp, couleur = C.Brume, interligne = 18.sp)
            }
            if (admin) {
                Surtitre("Membres")
                val liste = membres?.optJSONArray("membres") ?: JSONArray()
                for (i in 0 until liste.length()) {
                    val mb = liste.getJSONObject(i)
                    Row(
                        Modifier.fillMaxWidth().clip(RoundedCornerShape(12.dp)).background(C.Page).padding(horizontal = 12.dp, vertical = 8.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Column(Modifier.weight(1f)) {
                            T(mb.optString("nom").ifBlank { mb.optString("email") }, poids = FontWeight.ExtraBold, lignes = 1)
                            T(mb.optString("email") + if (!mb.optBoolean("vu")) " · pas encore connecté" else "", taille = 12.sp, couleur = C.Brume, lignes = 1)
                        }
                        Touche(onClick = { choixRole = mb.optString("email") }, bord = C.Trait2, rayon = 12.dp, actif = !occupe) {
                            T(nomRole(mb.optString("role")), couleur = C.Or, poids = FontWeight.ExtraBold)
                        }
                    }
                }
                Champ(courriel, { courriel = it.trim().take(120) }, "Gmail du membre", KeyboardType.Email)
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    NOMS_ROLES.forEach { (r, nom) ->
                        Touche(
                            onClick = { roleNouveau = r },
                            modifier = Modifier.weight(1f),
                            fond = if (roleNouveau == r) C.Marine else C.Carte,
                            bord = if (roleNouveau == r) null else C.Trait2,
                            rayon = 12.dp,
                            padding = androidx.compose.foundation.layout.PaddingValues(horizontal = 4.dp),
                        ) { T(nom, taille = 12.sp, poids = FontWeight.ExtraBold, couleur = if (roleNouveau == r) Color.White else C.Encre, lignes = 1) }
                    }
                }
                Touche(onClick = {
                    agir {
                        Comptes.api(store, "POST", "eglises/${geree.getString("id")}/membres", JSONObject().put("email", courriel).put("role", roleNouveau))
                        courriel = ""
                        chargerMembres()
                        Bulles.dire("Membre ajouté : il se connecte avec Google dans Siloé Stream.")
                    }
                }, modifier = Modifier.fillMaxWidth(), fond = C.Marine, actif = !occupe && courriel.contains("@")) {
                    T("Ajouter le membre", couleur = Color.White, poids = FontWeight.ExtraBold)
                }
            }
        }
        Touche(onClick = { agir { Comptes.deconnecter(store) } }, modifier = Modifier.fillMaxWidth(), fond = Color.Transparent) {
            T("Se déconnecter de Google", couleur = C.Brume, poids = FontWeight.Bold)
        }
    }
}
