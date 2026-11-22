#!/usr/bin/env bash
# Runs the EditMode suite inside a unityci/editor container (called by .github/workflows/unity-editmode.yml).
# Activation: a .ulf in UNITY_LICENSE if given, else a Personal named-user activation with UNITY_EMAIL/UNITY_PASSWORD
# through the editor's licensing client. The license is returned at exit.
# The editor log stays in the container; only error lines (secrets masked by Actions) are printed.
# Usage (inside the container, project mounted at the working directory): run-editmode.sh <results-dir>
set -u
out="${1:-unity-results}"
mkdir -p "$out"
log="$out/editor.log"
lc=/opt/unity/Editor/Data/Resources/Licensing/Client/Unity.Licensing.Client

activated=0
if [ -n "${UNITY_LICENSE:-}" ]; then
  for d in "$HOME/.local/share/unity3d/Unity" "$HOME/.config/unity3d/Unity"; do
    mkdir -p "$d" && printf '%s' "$UNITY_LICENSE" > "$d/Unity_lic.ulf"
  done
  activated=1
  echo "activation: .ulf from UNITY_LICENSE"
fi
if [ "$activated" = 0 ]; then
  for args in "--activate-ulf" "--activate-all"; do
    if "$lc" $args --include-personal --username "$UNITY_EMAIL" --password "$UNITY_PASSWORD" > "$out/activate.log" 2>&1; then
      activated=1
      echo "activation: licensing client $args"
      break
    fi
    echo "activation with $args failed:"
    grep -iE 'error|fail|denied|invalid|code' "$out/activate.log" | head -20
  done
fi
if [ "$activated" = 0 ]; then
  echo "::error::Unity license activation failed"
  exit 3
fi

return_license() {
  if [ -z "${UNITY_LICENSE:-}" ]; then
    "$lc" --return-ulf --username "$UNITY_EMAIL" --password "$UNITY_PASSWORD" > /dev/null 2>&1 \
      && echo "license returned" || echo "license return skipped"
  fi
}
trap return_license EXIT

unity-editor -batchmode -nographics -projectPath "$PWD" \
  -runTests -testPlatform EditMode -testResults "$PWD/$out/editmode-results.xml" -logFile "$log"
code=$?
echo "editor exit code: $code"
if [ ! -f "$out/editmode-results.xml" ]; then
  echo "::error::no test results; error lines from the editor log:"
  grep -E 'error CS|Exception|[Ll]icens|Aborting|Fatal' "$log" | grep -v '^\s*at ' | head -80
fi
exit "$code"
