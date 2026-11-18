#!/usr/bin/env bash
# Keeps the demo's containers away from private networks: a public demo lets visitors act as tenant admins, and tenant
# admins can make the API fetch identity-provider URLs. The API already refuses private addresses; this is the second
# layer. Containers may still answer incoming connections and reach the internet.
#
#   sudo deploy/demo/egress-firewall.sh install    rules now and after every boot (systemd unit)
#   sudo deploy/demo/egress-firewall.sh apply      rules now
#   sudo deploy/demo/egress-firewall.sh remove     take them out again
set -euo pipefail
BLOCK=(10.0.0.0/8 192.168.0.0/16 169.254.0.0/16 100.64.0.0/10)   # 172.16.0.0/12 is Docker's own range
TAG="gatehouse-demo-egress"

rules() {
  local op=$1
  iptables "$op" DOCKER-USER -m conntrack --ctstate ESTABLISHED,RELATED -m comment --comment "$TAG" -j RETURN
  for net in "${BLOCK[@]}"; do
    iptables "$op" DOCKER-USER -d "$net" -m comment --comment "$TAG" -j DROP
  done
}

remove() { while iptables -S DOCKER-USER | grep -q "$TAG"; do rules -D 2>/dev/null || break; done; }

case "${1:-}" in
  apply)
    iptables -L DOCKER-USER >/dev/null 2>&1 || iptables -N DOCKER-USER
    remove
    # Insert in reverse so the ESTABLISHED rule ends up first.
    for net in "${BLOCK[@]}"; do iptables -I DOCKER-USER -d "$net" -m comment --comment "$TAG" -j DROP; done
    iptables -I DOCKER-USER -m conntrack --ctstate ESTABLISHED,RELATED -m comment --comment "$TAG" -j RETURN
    iptables -S DOCKER-USER | grep "$TAG"
    ;;
  install)
    install -m 0755 "$(readlink -f "$0")" /usr/local/sbin/gatehouse-demo-egress
    cat > /etc/systemd/system/gatehouse-demo-egress.service <<UNIT
[Unit]
Description=Keep Gatehouse demo containers away from private networks
After=docker.service
Requires=docker.service

[Service]
Type=oneshot
RemainAfterExit=yes
ExecStart=/usr/local/sbin/gatehouse-demo-egress apply

[Install]
WantedBy=multi-user.target
UNIT
    systemctl daemon-reload
    systemctl enable --now gatehouse-demo-egress.service
    ;;
  remove) remove ;;
  *) sed -n '2,9p' "$0"; exit 2 ;;
esac
