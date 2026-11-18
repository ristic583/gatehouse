#!/usr/bin/env bash
# Brings a freshly seeded demo to life. The seed records Anna's access as confirmed in Gatehouse, but the simulated PACS
# start empty, so the first reconciliation reports it as "Missing in PACS". This script does what a security officer would:
# signs in as Jonas (one-click persona), and remediates each finding, so the connector agents grant the access in the PACS
# and the simulated building has people to walk through its doors. Run by setup.sh and reset.sh; safe to run again.
set -euo pipefail
cd "$(dirname "$0")"
set -a; . ./.env; set +a
# Through the public address, as a visitor would: with an https address the session cookie is Secure.
BASE="${PUBLIC_URL%/}"
ORIGIN="$BASE"
JAR="$(mktemp)"; trap 'rm -f "$JAR"' EXIT
deadline=$((SECONDS + ${WARMUP_TIMEOUT:-900}))

call() { curl -fsS -b "$JAR" -c "$JAR" "$@"; }

until call -o /dev/null "$BASE/login" 2>/dev/null; do
  (( SECONDS < deadline )) || { echo "warmup: the web app did not come up" >&2; exit 1; }
  sleep 5
done
until call -o /dev/null -H "Origin: $ORIGIN" --data-urlencode persona=jonas.nielsen@nordlys.test "$BASE/auth/demo" 2>/dev/null \
      && call -o /dev/null "$BASE/api/proxy/v1/tasks?status=Open" 2>/dev/null; do
  (( SECONDS < deadline )) || { echo "warmup: demo sign-in did not succeed" >&2; exit 1; }
  sleep 10
done

# Reconciliation runs shortly after start-up; wait for its findings, then remediate them.
while :; do
  ids=$(call "$BASE/api/proxy/v1/tasks?status=Open" | python3 -c 'import json,sys; print(" ".join(t["taskId"] for t in json.load(sys.stdin) if t.get("findingKind") == "MissingInPacs"))')
  [[ -n "$ids" ]] && break
  (( SECONDS < deadline )) || { echo "warmup: no reconciliation findings to remediate (already warm?)"; exit 0; }
  sleep 15
done
for id in $ids; do
  call -o /dev/null -H "Origin: $ORIGIN" -H "content-type: application/json" \
    -d '{"action":"Remediated","note":"Demo warm-up: grant the seeded access in the PACS"}' "$BASE/api/proxy/v1/tasks/$id/resolve"
  echo "warmup: remediated task $id"
done
