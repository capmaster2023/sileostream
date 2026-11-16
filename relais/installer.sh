#!/bin/bash
# Installe (ou met à jour) le relais Siloé Stream sur un VPS Ubuntu :
#   curl -fsSL https://raw.githubusercontent.com/capmaster2023/sileostream/main/relais/installer.sh | bash
# Avec votre propre nom de domaine (enregistrement A vers l'IP du VPS) : ... | bash -s relais.exemple.com
set -e
# tout le script est lu avant de commencer (curl | bash : apt ne peut pas avaler la suite)
principal() {
BRANCHE="${BRANCHE:-main}"
IP=$(curl -fsS -4 https://api.ipify.org || hostname -I | awk '{print $1}')
NOM="${1:-$(echo "$IP" | tr . -).sslip.io}"
echo "== Relais Siloé Stream : $NOM"
export DEBIAN_FRONTEND=noninteractive NEEDRESTART_MODE=a
apt-get update -q
apt-get install -y -q nodejs npm ufw curl gnupg debian-keyring debian-archive-keyring apt-transport-https
if ! command -v caddy >/dev/null; then
  curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | gpg --dearmor --yes -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
  curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' > /etc/apt/sources.list.d/caddy-stable.list
  apt-get update -q
  apt-get install -y -q caddy
fi
mkdir -p /opt/siloestream-relais && cd /opt/siloestream-relais
curl -fsSL "https://raw.githubusercontent.com/capmaster2023/sileostream/$BRANCHE/relais/relais.js" -o relais.js
curl -fsSL "https://raw.githubusercontent.com/capmaster2023/sileostream/$BRANCHE/relais/comptes.js" -o comptes.js
[ -f package.json ] || npm init -y >/dev/null
npm install --omit=dev ws@8 >/dev/null
id siloestream >/dev/null 2>&1 || useradd --system --no-create-home siloestream
cat > /etc/systemd/system/siloestream-relais.service <<UNIT
[Unit]
Description=Relais Siloé Stream
After=network.target
[Service]
ExecStart=/usr/bin/node /opt/siloestream-relais/relais.js
Restart=always
User=siloestream
# 1.9.1 : comptes et églises gardés dans /var/lib/siloe
StateDirectory=siloe
[Install]
WantedBy=multi-user.target
UNIT
cat > /etc/caddy/Caddyfile <<CADDY
$NOM {
    reverse_proxy 127.0.0.1:8080
}
CADDY
ufw allow 22/tcp >/dev/null; ufw allow 80/tcp >/dev/null; ufw allow 443/tcp >/dev/null; ufw --force enable >/dev/null
# ancien nom du relais : son service est arrêté et retiré (les comptes restent dans /var/lib/siloe)
if [ -f /etc/systemd/system/mychurch-relais.service ]; then
  systemctl disable --now mychurch-relais >/dev/null 2>&1 || true
  rm -f /etc/systemd/system/mychurch-relais.service
fi
systemctl daemon-reload
systemctl enable --now siloestream-relais
systemctl restart caddy
for i in $(seq 1 12); do
  if curl -fsS "https://$NOM/" >/dev/null 2>&1; then
    echo; echo "== OK : le relais Siloé Stream fonctionne sur https://$NOM"; echo "== Adresse à mettre dans Siloé Stream (Réglages › À distance) : $NOM"; return 0
  fi
  sleep 5
done
echo; echo "== Le relais tourne, mais le certificat HTTPS n'est pas encore prêt."
echo "== Détails : journalctl -u caddy --no-pager | tail -20"
}
principal "$@"
