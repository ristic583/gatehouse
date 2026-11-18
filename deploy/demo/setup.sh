#!/usr/bin/env bash
# Builds and starts the Gatehouse demo. Run from anywhere: deploy/demo/setup.sh
#   PUBLIC_URL      the address visitors use (default http://localhost:8088); fixed once .env exists
#   HTTP_PORT       host port of the entry (default 8088), HTTP_BIND the host address (default 127.0.0.1)
# State that is never committed lives next to this script: .env (generated secrets) and data/ (realms, gateway certificate).
set -euo pipefail
cd "$(dirname "$0")"

rand() { openssl rand -hex "$1"; }

if [ ! -f .env ]; then
  public="${PUBLIC_URL:-http://localhost:8088}"
  proto=https; [[ "$public" == http://* ]] && proto=http
  (umask 077; cat > .env <<ENV
PUBLIC_URL=$public
FORWARDED_PROTO=$proto
HTTP_PORT=${HTTP_PORT:-8088}
HTTP_BIND=${HTTP_BIND:-127.0.0.1}
POSTGRES_PASSWORD=$(rand 24)
REDIS_PASSWORD=$(rand 24)
RABBITMQ_PASSWORD=$(rand 24)
OBJECT_STORE_PASSWORD=$(rand 24)
SECRETS_MASTER_KEY=$(rand 32)
KEYCLOAK_ADMIN_PASSWORD=$(rand 24)
WEB_CLIENT_SECRET=$(rand 24)
BROKER_CLIENT_SECRET=$(rand 24)
ACME_BROKER_CLIENT_SECRET=$(rand 24)
PROVISIONER_CLIENT_SECRET=$(rand 24)
PLATFORM_WEB_CLIENT_SECRET=$(rand 24)
PLATFORM_OPERATOR_PASSWORD=Op-$(rand 12)
PLATFORM_OPERATOR_OTP_SECRET=$(rand 10)
DEMO_USER_PASSWORD=$(rand 16)
DEMO_CONNECTOR_TOKEN=$(rand 24)
DEMO_CONNECTOR_TOKEN_AARHUS=$(rand 24)
CLOUDPACS_API_KEY=$(rand 16)
ENV
  )
  echo "created deploy/demo/.env with generated secrets for $public"
fi
set -a; . ./.env; set +a

mkdir -p data/gateway
PUBLIC_URL="$PUBLIC_URL" python3 render-realms.py ../.. data
chmod -R a+rX data

# Self-signed TLS for the connector gateway; the demo agents trust exactly this certificate.
if [ ! -f data/gateway/gateway.pfx ]; then
  openssl req -x509 -newkey rsa:2048 -nodes -days 825 -subj "/CN=gatehouse-gateway" \
    -addext "subjectAltName=DNS:gateway,DNS:localhost" -keyout data/gateway/key.pem -out data/gateway/cert.pem 2>/dev/null
  openssl pkcs12 -export -out data/gateway/gateway.pfx -inkey data/gateway/key.pem -in data/gateway/cert.pem -passout pass:
  rm data/gateway/key.pem
  chmod 644 data/gateway/gateway.pfx data/gateway/cert.pem
fi

docker compose build
docker compose up -d --remove-orphans
echo "Gatehouse demo starting at $PUBLIC_URL (the first start takes a few minutes while Keycloak imports its realms)."
./warmup.sh
