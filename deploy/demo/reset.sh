#!/usr/bin/env bash
# Puts the demo back to its seeded state: drops every volume (databases, queues, files, agent enrolments) and starts again.
# Secrets in .env and the gateway certificate are kept. Meant for a nightly cron job, e.g. `0 4 * * * /path/to/reset.sh`.
set -euo pipefail
cd "$(dirname "$0")"
docker compose down -v --remove-orphans
docker compose up -d
./warmup.sh
