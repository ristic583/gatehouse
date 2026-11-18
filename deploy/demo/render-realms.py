#!/usr/bin/env python3
"""Render the demo's Keycloak realms from infra/keycloak (the development realms stay the source of truth).

Development uses three origins (web :3000, Keycloak :8080, the demo companies' IdP :8081); the demo serves all three behind
one public URL: the web app at /, Gatehouse's Keycloak at /kc and the demo companies' IdP at /idp. Secrets stay as ${GH_...}
placeholders that Keycloak resolves from its environment at import.

Usage: render-realms.py <repo-root> <out-dir>   (reads PUBLIC_URL)
"""
import json
import os
import sys

root, out = sys.argv[1], sys.argv[2]
public = os.environ["PUBLIC_URL"].rstrip("/")
REWRITES = [
    ("http://localhost:8080/realms", f"{public}/kc/realms"),
    ("http://localhost:8081/realms", f"{public}/idp/realms"),
    ("http://localhost:3000", public),
]


def rewrite(value):
    if isinstance(value, str):
        for old, new in REWRITES:
            value = value.replace(old, new)
        return value
    if isinstance(value, list):
        return [rewrite(v) for v in value]
    if isinstance(value, dict):
        return {k: rewrite(v) for k, v in value.items()}
    return value


for source, target in (("realms", "realms"), ("upstream", "upstream")):
    os.makedirs(os.path.join(out, target), exist_ok=True)
    folder = os.path.join(root, "infra", "keycloak", source)
    for name in sorted(os.listdir(folder)):
        if not name.endswith(".json"):
            continue
        with open(os.path.join(folder, name), encoding="utf-8") as f:
            realm = rewrite(json.load(f))
        realm["sslRequired"] = "external"
        with open(os.path.join(out, target, name), "w", encoding="utf-8") as f:
            json.dump(realm, f, indent=2)
