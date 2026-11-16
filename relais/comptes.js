// Siloé Stream 1.9.1 : comptes Google, églises, membres et rôles (API HTTPS du relais).
// Chaque église : un ou plusieurs admins, des membres (Versets, Chants, Affichage, Caméraman) ajoutés par leur Gmail, et le PC de l'église.
// 1.9.3 : les mêmes comptes se connectent au logiciel du PC (siloestream.tech/connexion) ; Affichage = l'église et Zoom.
// Le PC donne un secret au moment de la liaison ; le serveur signe avec lui des billets de 24 h pour les téléphones des membres.
// Le PC vérifie le billet lui-même : un membre retiré n'obtient plus de billet et ne peut plus entrer.
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const CLIENT_ID = process.env.GOOGLE_CLIENT_ID || '651141034269-s9hq1jb8bafbiipoprof0v1qm2175l9d.apps.googleusercontent.com';
const DOSSIER = process.env.STATE_DIRECTORY || process.env.DONNEES || path.join(__dirname, 'donnees');
const FICHIER = path.join(DOSSIER, 'comptes.json');
const ROLES = ['admin', 'bible', 'chants', 'affichage', 'camera'];
const JOUR = 86400;

let d = { comptes: {}, sessions: {}, eglises: {}, codes: {} };
try { d = Object.assign(d, JSON.parse(fs.readFileSync(FICHIER, 'utf8'))); } catch {}
let minuterie = null;
function sauver() {
  if (minuterie) return;
  minuterie = setTimeout(() => {
    minuterie = null;
    try {
      fs.mkdirSync(DOSSIER, { recursive: true });
      fs.writeFileSync(FICHIER + '.tmp', JSON.stringify(d));
      fs.renameSync(FICHIER + '.tmp', FICHIER);
    } catch (e) { console.error('comptes : sauvegarde', e.message); }
  }, 200);
}
const maintenant = () => Math.floor(Date.now() / 1000);
const b64 = b => Buffer.from(b).toString('base64url');
const hasard = n => b64(crypto.randomBytes(n));
const hmac = (cle, msg) => crypto.createHmac('sha256', cle).update(msg).digest();
const courriel = e => String(e || '').trim().toLowerCase();
const okCourriel = e => /^[^\s@]{1,64}@[^\s@]{1,190}\.[a-z]{2,}$/.test(e);

// petites limites : 60 demandes par minute et par adresse
const debits = new Map();
setInterval(() => debits.clear(), 60000);
function tropVite(req) {
  const ip = req.headers['x-forwarded-for']?.split(',')[0].trim() || req.socket.remoteAddress || '?';
  const n = (debits.get(ip) || 0) + 1;
  debits.set(ip, n);
  return n > 60;
}

async function verifierGoogle(idToken) {
  const r = await fetch('https://oauth2.googleapis.com/tokeninfo?id_token=' + encodeURIComponent(idToken));
  if (!r.ok) return null;
  const j = await r.json();
  if (j.aud !== CLIENT_ID) return null;
  if (!['accounts.google.com', 'https://accounts.google.com'].includes(j.iss)) return null;
  if (j.email_verified !== 'true' && j.email_verified !== true) return null;
  if (+j.exp < maintenant()) return null;
  return { email: courriel(j.email), nom: j.name || j.email, photo: j.picture || '' };
}

function moi(email) {
  const c = d.comptes[email] || {};
  const eglises = Object.entries(d.eglises)
    .filter(([, e]) => e.membres[email])
    .map(([id, e]) => ({ id, nom: e.nom, role: e.membres[email], pc: e.pc ? { pcId: e.pc.pcId, nom: e.pc.nom } : null }));
  return { email, nom: c.nom || email, photo: c.photo || '', eglises };
}

function lireCorps(req) {
  return new Promise((ok, ko) => {
    let t = '';
    req.on('data', c => { t += c; if (t.length > 64 * 1024) { ko(new Error('trop gros')); req.destroy(); } });
    req.on('end', () => { try { ok(t ? JSON.parse(t) : {}); } catch { ok({}); } });
    req.on('error', ko);
  });
}

async function api(req, res, pcs) {
  const repondre = (code, o) => {
    res.writeHead(code, { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store' });
    res.end(JSON.stringify(o));
  };
  const erreur = (code, msg) => repondre(code, { erreur: msg });
  try {
    if (tropVite(req)) return erreur(429, 'Trop de demandes : réessayez dans une minute.');
    const parts = (req.url || '').split('?')[0].split('/').filter(Boolean).slice(1).map(decodeURIComponent); // après « api »
    const m = req.method;
    const corps = m === 'POST' || m === 'DELETE' ? await lireCorps(req) : {};

    // --- sans compte
    if (m === 'POST' && parts[0] === 'connexion') {
      const g = await verifierGoogle(String(corps.idToken || ''));
      if (!g) return erreur(401, 'Connexion Google refusée.');
      d.comptes[g.email] = { nom: g.nom, photo: g.photo, vu: maintenant() };
      const jeton = hasard(32);
      d.sessions[jeton] = { email: g.email, exp: maintenant() + 60 * JOUR };
      sauver();
      return repondre(200, { jeton, ...moi(g.email) });
    }
    if (m === 'POST' && parts[0] === 'pc' && parts[1] === 'lier') {
      // le PC de l'église, avec le code à usage unique que l'admin a fait passer par son téléphone
      const code = String(corps.code || '').toUpperCase();
      const c = d.codes[code];
      if (!c || c.exp < maintenant()) return erreur(400, 'Code expiré : recommencez depuis le téléphone.');
      const e = d.eglises[c.eglise];
      if (!e) return erreur(400, 'Église introuvable.');
      if (!/^[A-Za-z0-9_\-]{4,128}$/.test(corps.pcId || '') || !/^[A-Za-z0-9_\-]{40,64}$/.test(corps.secret || ''))
        return erreur(400, 'Demande invalide.');
      delete d.codes[code];
      e.pc = { pcId: corps.pcId, nom: String(corps.nom || 'PC').slice(0, 60), secret: corps.secret, lie: maintenant() };
      sauver();
      return repondre(200, { eglise: c.eglise, nom: e.nom });
    }

    // --- avec compte (Authorization: Bearer <jeton>)
    const jeton = (req.headers.authorization || '').replace(/^Bearer\s+/i, '');
    const s = d.sessions[jeton];
    if (!s || s.exp < maintenant()) return erreur(401, 'Session expirée : reconnectez-vous avec Google.');
    const email = s.email;

    if (m === 'GET' && parts[0] === 'moi') return repondre(200, moi(email));
    if (m === 'POST' && parts[0] === 'deconnexion') { delete d.sessions[jeton]; sauver(); return repondre(200, {}); }
    if (m === 'POST' && parts[0] === 'eglises' && parts.length === 1) {
      const nom = String(corps.nom || '').trim().slice(0, 80);
      if (nom.length < 2) return erreur(400, 'Donnez un nom à votre église.');
      if (Object.values(d.eglises).filter(e => e.createur === email).length >= 5) return erreur(400, 'Maximum 5 églises par compte.');
      const id = hasard(9);
      d.eglises[id] = { nom, createur: email, cree: maintenant(), membres: { [email]: 'admin' }, pc: null };
      sauver();
      return repondre(200, { id, ...moi(email) });
    }

    const e = parts[0] === 'eglises' ? d.eglises[parts[1]] : null;
    if (!e || !e.membres[email]) return erreur(404, 'Église introuvable.');
    const admin = e.membres[email] === 'admin';
    const action = parts[2];

    if (m === 'POST' && action === 'billet') {
      // le téléphone d'un membre demande l'entrée au PC de l'église
      if (!e.pc) return erreur(400, "Le PC de l'église n'est pas encore relié : l'admin doit le relier depuis son téléphone.");
      const dev = String(corps.deviceId || '');
      if (!/^[A-Za-z0-9_\-]{4,128}$/.test(dev)) return erreur(400, 'Téléphone invalide.');
      const secret = Buffer.from(e.pc.secret, 'base64url');
      const exp = maintenant() + JOUR;
      const charge = b64(JSON.stringify({ pc: e.pc.pcId, dev, email, role: e.membres[email], nom: (d.comptes[email] || {}).nom || email, exp }));
      const billet = charge + '.' + b64(hmac(secret, 'siloe-billet|' + charge));
      const cle = b64(hmac(secret, `siloe-compte|${e.pc.pcId}|${dev}`));
      return repondre(200, { billet, cle, exp, role: e.membres[email], pcId: e.pc.pcId, pcNom: e.pc.nom, relais: req.headers.host, enLigne: pcs.has(e.pc.pcId) });
    }
    if (!admin) return erreur(403, "Réservé à l'admin de l'église.");
    if (m === 'POST' && action === 'code-pc') {
      for (const [k, v] of Object.entries(d.codes)) if (v.exp < maintenant()) delete d.codes[k];
      const code = crypto.randomBytes(5).toString('hex').toUpperCase();
      d.codes[code] = { eglise: parts[1], exp: maintenant() + 600 };
      sauver();
      return repondre(200, { code, relais: req.headers.host });
    }
    if (m === 'GET' && action === 'membres') {
      return repondre(200, {
        nom: e.nom,
        pc: e.pc ? { pcId: e.pc.pcId, nom: e.pc.nom } : null,
        membres: Object.entries(e.membres).map(([em, role]) => ({ email: em, role, nom: (d.comptes[em] || {}).nom || '', vu: !!d.comptes[em] })),
      });
    }
    if (m === 'POST' && action === 'membres') {
      const em = courriel(corps.email);
      const role = String(corps.role || '');
      if (!okCourriel(em)) return erreur(400, 'Adresse Gmail invalide.');
      if (!ROLES.includes(role)) return erreur(400, 'Rôle invalide.');
      if (Object.keys(e.membres).length >= 50 && !e.membres[em]) return erreur(400, 'Maximum 50 membres.');
      if (em === email && role !== 'admin' && Object.values(e.membres).filter(r => r === 'admin').length < 2)
        return erreur(400, "Gardez au moins un admin : nommez-en un autre d'abord.");
      e.membres[em] = role;
      sauver();
      return repondre(200, { ok: true });
    }
    if (m === 'DELETE' && action === 'membres') {
      const em = courriel(parts[3] || corps.email);
      if (!e.membres[em]) return erreur(404, 'Membre introuvable.');
      if (e.membres[em] === 'admin' && Object.values(e.membres).filter(r => r === 'admin').length < 2)
        return erreur(400, "Gardez au moins un admin : nommez-en un autre d'abord.");
      delete e.membres[em];
      sauver();
      return repondre(200, { ok: true });
    }
    if (m === 'POST' && action === 'renommer') {
      const nom = String(corps.nom || '').trim().slice(0, 80);
      if (nom.length < 2) return erreur(400, 'Nom trop court.');
      e.nom = nom;
      sauver();
      return repondre(200, { ok: true });
    }
    if (m === 'POST' && action === 'delier-pc') { e.pc = null; sauver(); return repondre(200, { ok: true }); }
    return erreur(404, 'Inconnu.');
  } catch (x) {
    console.error('api', x);
    return erreur(500, 'Erreur du serveur.');
  }
}

module.exports = { api, _donnees: () => d };
