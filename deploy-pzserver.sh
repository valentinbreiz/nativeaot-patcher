#!/bin/bash
# Déploiement Project Zomboid Build 42 unstable sur Debian 13
# Idempotent — peut être relancé sans casser une install existante.
# À exécuter en root (ou via sudo) sur un VPS Debian 13 fraîchement provisionné.
#
# Pré-requis :
#   - Debian 13 (Trixie)
#   - User admin (membre du groupe sudo) déjà créé avec clé SSH
#   - Variables d'environnement requises avant exécution :
#       export ADMIN_USER="zomboid"            # user sudo humain (pour aliases)
#       export PZ_ADMIN_PASS="DZh5R3@04E@DYeFT" # mot de passe admin in-game
#       export PZ_SERVER_PASS=""                # mot de passe join (optionnel, "" = public)
#       export PZ_PUBLIC_NAME="Oumoud"
#       export PZ_PUBLIC_DESC="Coupe d'Oumoud"
#
# Usage : sudo -E bash deploy-pzserver.sh

set -euo pipefail

# ---------- Variables ----------
ADMIN_USER="${ADMIN_USER:-zomboid}"
PZ_ADMIN_PASS="${PZ_ADMIN_PASS:?PZ_ADMIN_PASS is required}"
PZ_SERVER_PASS="${PZ_SERVER_PASS:-}"
PZ_PUBLIC_NAME="${PZ_PUBLIC_NAME:-Oumoud}"
PZ_PUBLIC_DESC="${PZ_PUBLIC_DESC:-Coupe d'Oumoud}"
PZ_HEAP_MIN="${PZ_HEAP_MIN:-4g}"
PZ_HEAP_MAX="${PZ_HEAP_MAX:-8g}"
PZ_MAX_PLAYERS="${PZ_MAX_PLAYERS:-10}"
PZ_APPID=380870
PZ_BETA="unstable"

PZHOME=/home/pzserver
PZINST=$PZHOME/pzserver
PZDATA=$PZHOME/Zomboid

if [[ $EUID -ne 0 ]]; then
    echo "ERROR: must be run as root" >&2
    exit 1
fi

log() { echo -e "\033[1;36m[+] $*\033[0m"; }

# ---------- 1. SSH hardening ----------
log "1. SSH hardening (PermitRootLogin no, PasswordAuthentication no)"
cat > /etc/ssh/sshd_config.d/00-hardening.conf <<EOF
# Managed by deploy-pzserver — do not edit by hand
PermitRootLogin no
PasswordAuthentication no
PubkeyAuthentication yes
KbdInteractiveAuthentication no
EOF
sshd -t
systemctl reload ssh

# ---------- 1b. Swapfile 4G (no swap on stock CLASSIC-16) ----------
log "1b. Swapfile 4G"
if [[ ! -f /swapfile ]]; then
    fallocate -l 4G /swapfile
    chmod 600 /swapfile
    mkswap /swapfile
    swapon /swapfile
    grep -q /swapfile /etc/fstab || echo "/swapfile none swap sw 0 0" >> /etc/fstab
fi

# ---------- 2. Update + base packages ----------
log "2. apt update + paquets de base"
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq
apt-get full-upgrade -y -qq
apt-get install -y -qq \
    ufw fail2ban screen htop unzip rsync cron curl wget \
    ca-certificates gnupg lsb-release

# Timezone + locales
timedatectl set-timezone Europe/Paris
sed -i -E 's/^# (fr_FR.UTF-8|en_US.UTF-8) UTF-8/\1 UTF-8/' /etc/locale.gen
locale-gen
update-locale LANG=en_US.UTF-8

# ---------- 3. UFW + fail2ban ----------
log "3. UFW + fail2ban"
ufw --force reset
ufw default deny incoming
ufw default allow outgoing
ufw allow 22/tcp
ufw allow 16261/udp
ufw allow 16262/udp
ufw --force enable
systemctl enable --now fail2ban

# ---------- 4. Steam env (i386 + non-free) ----------
log "4. dpkg i386 + non-free + steamcmd"
sed -i 's/^Components: main$/Components: main contrib non-free non-free-firmware/' \
    /etc/apt/sources.list.d/debian.sources
dpkg --add-architecture i386
echo steam steam/license note in &debconf/priority=critical | debconf-set-selections
echo steam steam/question select "I AGREE" | debconf-set-selections
apt-get update -qq
apt-get install -y -qq \
    default-jre-headless lib32gcc-s1 lib32stdc++6 libsdl2-2.0-0:i386 steamcmd

# ---------- 5. User pzserver ----------
log "5. Création user pzserver"
if ! id pzserver &>/dev/null; then
    useradd -r -m -d $PZHOME -s /bin/bash pzserver
    passwd -l pzserver
fi

# ---------- 6. Install PZ via SteamCMD ----------
log "6. SteamCMD app_update $PZ_APPID -beta $PZ_BETA"
mkdir -p $PZINST
chown -R pzserver:pzserver $PZINST
# Run twice — first run self-updates steamcmd, may fail with "Missing configuration"
sudo -u pzserver -H /usr/games/steamcmd \
    +@sSteamCmdForcePlatformType linux \
    +force_install_dir $PZINST \
    +login anonymous \
    +app_update $PZ_APPID -beta $PZ_BETA validate \
    +quit || true
sudo -u pzserver -H /usr/games/steamcmd \
    +@sSteamCmdForcePlatformType linux \
    +force_install_dir $PZINST \
    +login anonymous \
    +app_update $PZ_APPID -beta $PZ_BETA validate \
    +quit

# ---------- 7. Heap Java ----------
log "7. Tune heap dans ProjectZomboid64.json (-Xms$PZ_HEAP_MIN -Xmx$PZ_HEAP_MAX)"
sudo -u pzserver python3 - <<PYEOF
import json
p = "$PZINST/ProjectZomboid64.json"
d = json.load(open(p))
args = [a for a in d["vmArgs"] if not a.startswith("-Xmx") and not a.startswith("-Xms")]
args.insert(1, "-Xms$PZ_HEAP_MIN")
args.insert(2, "-Xmx$PZ_HEAP_MAX")
d["vmArgs"] = args
json.dump(d, open(p, "w"), indent=4)
PYEOF

# ---------- 6b. First launch (only if config does not exist yet) ----------
if [[ ! -f $PZDATA/Server/servertest.ini ]]; then
    log "6b. Premier lancement (génère world + admin pass)"
    cat > /tmp/pz-firstlaunch.sh <<EOF2
#!/bin/bash
cd $PZINST
screen -S pz -X quit 2>/dev/null || true; sleep 1
screen -dmS pz ./start-server.sh
sleep 10
screen -S pz -X stuff '$PZ_ADMIN_PASS\\015'
sleep 2
screen -S pz -X stuff '$PZ_ADMIN_PASS\\015'
for i in \$(seq 1 90); do
    grep -q "SERVER STARTED" $PZDATA/server-console.txt 2>/dev/null && break
    sleep 5
done
sleep 5
screen -S pz -X stuff 'save\\015'; sleep 3
screen -S pz -X stuff 'quit\\015'
for i in \$(seq 1 60); do
    screen -ls 2>/dev/null | grep -q '\\.pz' || exit 0
    sleep 2
done
exit 0
EOF2
    chmod +x /tmp/pz-firstlaunch.sh
    cp /tmp/pz-firstlaunch.sh $PZHOME/pz-firstlaunch.sh
    chown pzserver:pzserver $PZHOME/pz-firstlaunch.sh
    sudo -u pzserver -H bash $PZHOME/pz-firstlaunch.sh
fi

# Configure servertest.ini
log "Configure servertest.ini (PublicName, MaxPlayers, Public, Password)"
INI=$PZDATA/Server/servertest.ini
sudo -u pzserver python3 - <<PYEOF
import re
p = "$INI"
with open(p) as f:
    content = f.read()
def setk(c, k, v):
    return re.sub(r'^' + re.escape(k) + r'=.*$', f'{k}={v}', c, count=1, flags=re.M)
content = setk(content, "PublicName",        "$PZ_PUBLIC_NAME")
content = setk(content, "PublicDescription", "$PZ_PUBLIC_DESC")
content = setk(content, "Public",            "true")
content = setk(content, "MaxPlayers",        "$PZ_MAX_PLAYERS")
content = setk(content, "Password",          "$PZ_SERVER_PASS")
open(p, 'w').write(content)
PYEOF

# Save credentials
cat > /root/pz-credentials.txt <<EOF3
Project Zomboid B42 — Server credentials
Server name : $PZ_PUBLIC_NAME
Description : $PZ_PUBLIC_DESC
IP          : $(hostname -I | awk '{print $1}')
Game port   : 16261/UDP
Query port  : 16262/UDP
Admin user  : admin
Admin pass  : $PZ_ADMIN_PASS
Server pass : ${PZ_SERVER_PASS:-(none — public)}
EOF3
chmod 600 /root/pz-credentials.txt

# ---------- 8. systemd service ----------
log "8. systemd service pzserver"
cat > /etc/systemd/system/pzserver.service <<'EOF4'
[Unit]
Description=Project Zomboid B42 Dedicated Server
After=network-online.target
Wants=network-online.target

[Service]
Type=forking
User=pzserver
Group=pzserver
WorkingDirectory=/home/pzserver/pzserver
ExecStart=/usr/bin/screen -dmS pz /home/pzserver/pzserver/start-server.sh
ExecStop=/usr/bin/screen -S pz -X stuff "save\012quit\012"
TimeoutStopSec=300
Restart=on-failure
RestartSec=30
KillMode=mixed
MemoryMax=12G
MemoryHigh=11G

[Install]
WantedBy=multi-user.target
EOF4
systemctl daemon-reload
systemctl enable --now pzserver

# ---------- 10. Backup script ----------
log "10. Script backup"
cat > /usr/local/bin/pzbackup.sh <<'EOF5'
#!/bin/bash
set -euo pipefail
BACKUP_DIR=/var/backups/pzserver
DATE=$(date +%Y%m%d-%H%M%S)
mkdir -p "$BACKUP_DIR"
tar -czf "$BACKUP_DIR/pz-backup-$DATE.tar.gz" \
    -C /home/pzserver Zomboid/Saves Zomboid/Server Zomboid/db 2>/dev/null || true
find "$BACKUP_DIR" -name 'pz-backup-*.tar.gz' -mtime +7 -delete
echo "[$(date '+%Y-%m-%d %H:%M:%S')] Backup completed: pz-backup-$DATE.tar.gz" >> /var/log/pzbackup.log
EOF5
chmod 755 /usr/local/bin/pzbackup.sh
touch /var/log/pzbackup.log /var/log/pzserver-restart.log

# ---------- 9. Cron ----------
log "9. Cron restart 5h + backup 4h"
( crontab -l 2>/dev/null | grep -v -E "pzserver|pzbackup" ;
  echo "0 4 * * * /usr/local/bin/pzbackup.sh" ;
  echo "0 5 * * * /bin/systemctl restart pzserver >> /var/log/pzserver-restart.log 2>&1" ) | crontab -

# ---------- 11. Aliases ----------
log "11. Aliases pour $ADMIN_USER"
ALIASES='# Project Zomboid helpers
alias pzconsole="sudo -u pzserver screen -r pz"
alias pzlog="sudo tail -f /home/pzserver/Zomboid/server-console.txt"
alias pzstatus="systemctl status pzserver"
alias pzrestart="sudo systemctl restart pzserver"
alias pzstop="sudo systemctl stop pzserver"
alias pzstart="sudo systemctl start pzserver"
alias pzbackup="sudo /usr/local/bin/pzbackup.sh"
alias pzconfig="sudo -u pzserver nano /home/pzserver/Zomboid/Server/servertest.ini"'
HOMEDIR=$(getent passwd "$ADMIN_USER" | cut -d: -f6)
for rc in "$HOMEDIR/.zshrc" "$HOMEDIR/.bashrc"; do
    if [[ -f "$rc" ]] && ! grep -q "Project Zomboid helpers" "$rc"; then
        printf "\n%s\n" "$ALIASES" >> "$rc"
        chown "$ADMIN_USER:$ADMIN_USER" "$rc"
    fi
done

# ---------- 13. Validation ----------
log "13. Validation finale"
systemctl is-active pzserver
systemctl is-enabled pzserver
systemctl is-enabled fail2ban
ss -lnup | grep -E '16261|16262'
ufw status verbose | head -10
echo
echo "Déploiement terminé. Credentials : /root/pz-credentials.txt"
echo "Doc : /root/README-pzserver.md (à copier manuellement si besoin)"
