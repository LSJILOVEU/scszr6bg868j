#!/bin/sh
# Builds the exporter, runs the given setups (default: base playtest) and puts the replays next to the page in out/.
# Usage: tools/replay/run.sh [setup ...]   env: TICKS (default 2400), EVERY (default 3)
set -eu
here=$(cd "$(dirname "$0")" && pwd)
dotnet build "$here/WarCore.Replay.csproj" -c Release -nologo -v quiet >/dev/null
bin="$here/bin/Release/net8.0/WarCore.Replay"
mkdir -p "$here/out"
[ $# -gt 0 ] || set -- base playtest
for setup in "$@"; do "$bin" "$setup" "${TICKS:-2400}" "${EVERY:-3}" "$here/out/replay-$setup.json"; done
cp "$here/battle-replay.html" "$here/out/"
echo "open: cd $here/out && python3 -m http.server, then /battle-replay.html"
