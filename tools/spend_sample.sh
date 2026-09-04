#!/usr/bin/env bash
set -a; . "$HOME/.hermes/.env" 2>/dev/null; set +a
curl -s -m 12 https://openrouter.ai/api/v1/auth/key -H "Authorization: Bearer ${OPENROUTER_API_KEY:-}" \
 | python3 -c "import json,sys; print(json.load(sys.stdin)['data'].get('usage_daily',''))" 2>/dev/null
