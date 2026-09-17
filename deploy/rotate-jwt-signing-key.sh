#!/bin/bash
# Xoay Jwt__SigningKey trên staging — KHÔNG echo giá trị khóa.
set -euo pipefail
ENV=/home/deploy/app/deploy/.env
COMPOSE="sudo docker compose -f /home/deploy/app/deploy/docker-compose.staging.apache.yml --project-directory /home/deploy/app/deploy"

sudo test -f "$ENV"
sudo cp -a "$ENV" "${ENV}.bak-signing-$(date -u +%Y%m%dT%H%M%SZ)"

NEW_KEY=$(openssl rand -base64 48)
export NEW_KEY

# .env staging có thể không phải UTF-8 thuần (byte >127) — đọc/ghi latin-1 giữ nguyên byte.
sudo -E python3 <<'PY'
import base64, os
from pathlib import Path
path = Path("/home/deploy/app/deploy/.env")
key = os.environ["NEW_KEY"]
raw = base64.b64decode(key)
if len(raw) < 32:
    raise SystemExit(f"key too short: {len(raw)} bytes")
text = path.read_bytes().decode("latin-1")
lines = text.splitlines()
out, replaced = [], False
for line in lines:
    if line.startswith("Jwt__SigningKey="):
        out.append("Jwt__SigningKey=" + key)
        replaced = True
    else:
        out.append(line)
if not replaced:
    raise SystemExit("Jwt__SigningKey= not found")
path.write_bytes(("\n".join(out) + "\n").encode("latin-1"))
print("Jwt__SigningKey: ROTATED")
print("key_bytes=", len(raw))
PY
unset NEW_KEY

$COMPOSE up -d api --force-recreate
for i in $(seq 1 15); do
  h=$(sudo docker inspect socialapp-staging-api-1 --format '{{.State.Health.Status}}' 2>/dev/null || echo starting)
  echo "health=$h"
  [ "$h" = healthy ] && break
  sleep 2
done
curl -fsS https://mxh.banhgao.net/health/ready
echo
echo "ROTATE_OK"
