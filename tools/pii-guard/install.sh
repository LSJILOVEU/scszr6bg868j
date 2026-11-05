#!/bin/sh
# One-time setup after cloning: use the repository hooks and the neutral git identity.
set -e
cd "$(git rev-parse --show-toplevel)"
git config core.hooksPath tools/pii-guard/hooks
git config user.name warcore
git config user.email noreply@users.noreply.github.com
chmod +x tools/pii-guard/hooks/* tools/pii-guard/pii_guard.py
python3 -I tools/pii-guard/pii_guard.py --self-test
echo "Hooks installed. Commit with: TZ=UTC git commit -m '<short technical message>'"
echo "Optional private term list: put one term per line in ~/.warcore-pii-terms (never commit it)."
