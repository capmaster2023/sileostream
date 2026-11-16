// Relais Siloé Stream (1.6.9) : relie un téléphone, n'importe où sur Internet, au PC de l'église.
// Le PC garde une connexion « contrôle » ouverte (/pc/<pcId>). Un téléphone qui arrive (/telephone/<pcId>) reçoit un canal :
// le PC ouvre /canal/<id>, et le relais passe les messages tels quels dans les deux sens. Le relais ne lit rien :
// le jumelage (clé HMAC) se fait entre le téléphone et le PC, comme sur le Wi-Fi de l'église.
const http = require('http');
const crypto = require('crypto');
const { WebSocketServer } = require('ws');

const PORT = +(process.env.PORT || 8080);
const pcs = new Map();      // pcId -> ws contrôle
const attente = new Map();  // canal -> { tel, file, minuterie }

const VERSION = 2;
const comptes = require('./comptes');
const serveur = http.createServer((req, res) => {
  // 1.9.1 : comptes Google, églises et rôles
  if ((req.url || '').startsWith('/api/')) { comptes.api(req, res, pcs); return; }
  // /etat/<pcId> : Siloé Stream (Réglages) vérifie que son PC est joignable de l'extérieur
  const [, type, id] = (req.url || '').split('?')[0].split('/');
  if (type === 'etat' && ok(id)) {
    res.writeHead(200, { 'content-type': 'application/json', 'cache-control': 'no-store' });
    res.end(JSON.stringify({ relais: 'siloestream', version: VERSION, pc: pcs.has(id) }));
    return;
  }
  res.writeHead(200, { 'content-type': 'text/plain; charset=utf-8' });
  res.end(`Relais Siloé Stream (version ${VERSION}) : en marche\n`);
});
const wss = new WebSocketServer({ noServer: true, maxPayload: 16 * 1024 * 1024 });
const ok = id => /^[A-Za-z0-9_\-]{4,128}$/.test(id || '');

serveur.on('upgrade', (req, sock, tete) => {
  const [, type, id] = (req.url || '').split('?')[0].split('/');
  if (!['pc', 'telephone', 'canal'].includes(type) || !ok(id)) { sock.destroy(); return; }
  wss.handleUpgrade(req, sock, tete, ws => brancher(ws, type, id));
});

function vivant(ws) {
  ws.vivant = true;
  ws.on('pong', () => { ws.vivant = true; });
}

function brancher(ws, type, id) {
  vivant(ws);
  if (type === 'pc') {
    const ancien = pcs.get(id);
    if (ancien) try { ancien.close(4000, 'remplacé'); } catch {}
    pcs.set(id, ws);
    ws.on('close', () => { if (pcs.get(id) === ws) pcs.delete(id); });
    ws.on('message', () => {});
    return;
  }
  if (type === 'telephone') {
    const pc = pcs.get(id);
    if (!pc) { ws.close(4404, 'PC hors ligne'); return; }
    const canal = crypto.randomBytes(16).toString('hex');
    const e = { tel: ws, file: [], minuterie: setTimeout(() => { attente.delete(canal); try { ws.close(4408, 'PC sans réponse'); } catch {} }, 10000) };
    attente.set(canal, e);
    ws.on('message', (m, bin) => e.file.push([m, bin]));
    ws.on('close', () => { if (attente.get(canal) === e) { clearTimeout(e.minuterie); attente.delete(canal); } });
    pc.send(JSON.stringify({ t: 'canal', id: canal }));
    return;
  }
  // canal : le PC répond pour un téléphone
  const e = attente.get(id);
  if (!e) { ws.close(4404, 'canal inconnu'); return; }
  attente.delete(id);
  clearTimeout(e.minuterie);
  const tel = e.tel;
  tel.removeAllListeners('message');
  for (const [m, bin] of e.file) ws.send(m, { binary: bin });
  tel.on('message', (m, bin) => { if (ws.readyState === 1) ws.send(m, { binary: bin }); });
  ws.on('message', (m, bin) => { if (tel.readyState === 1) tel.send(m, { binary: bin }); });
  const fermer = () => { try { tel.close(); } catch {} try { ws.close(); } catch {} };
  tel.on('close', fermer);
  ws.on('close', fermer);
}

setInterval(() => {
  for (const ws of wss.clients) {
    if (!ws.vivant) { ws.terminate(); continue; }
    ws.vivant = false;
    try { ws.ping(); } catch {}
  }
}, 20000);

serveur.listen(PORT, '127.0.0.1', () => console.log(`relais SiloeStream sur 127.0.0.1:${PORT}`));
