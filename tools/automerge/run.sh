#!/usr/bin/env bash
# Called by .github/workflows/automerge.yml. Env: GH_TOKEN, REPO, SHA (head commit of the event), PRS (pull request numbers).
# Fetches the current state of each pull request, lets decide.py judge it, and squash-merges with an explicit empty body
# so GitHub does not append its default trailer lines.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
status=0
for pr in $PRS; do
  dir="$(mktemp -d)"
  gh pr view "$pr" --repo "$REPO" --json number,title,state,isDraft,baseRefName,headRefOid,labels,changedFiles > "$dir/pr.json"
  gh api "repos/$REPO/pulls/$pr/files?per_page=100" --paginate \
    --jq '.[] | {filename, previous_filename}' | jq -s . > "$dir/files.json"
  gh api "repos/$REPO/commits/$SHA/check-runs?per_page=100" --paginate \
    --jq '.check_runs[] | {id, name, status, conclusion}' | jq -s . > "$dir/checks.json"
  rc=0
  python3 -I "$here/decide.py" --pr "$dir/pr.json" --files "$dir/files.json" --checks "$dir/checks.json" --sha "$SHA" || rc=$?
  case "$rc" in
    0) ;;
    10|11) continue ;;
    *) echo "::error::#$pr: decide.py failed with exit code $rc"; status=1; continue ;;
  esac
  subject="$(jq -r .title "$dir/pr.json") (#$pr)"
  gh pr merge "$pr" --repo "$REPO" --squash --match-head-commit "$SHA" --subject "$subject" --body "" \
    || [ "$(gh pr view "$pr" --repo "$REPO" --json state --jq .state)" = MERGED ]
  # The merge commit message must be exactly the subject line.
  oid="$(gh pr view "$pr" --repo "$REPO" --json mergeCommit --jq .mergeCommit.oid)"
  msg="$(gh api "repos/$REPO/commits/$oid" --jq .commit.message)"
  if [ "$msg" = "$subject" ]; then
    echo "#$pr: merged as $oid"
  else
    echo "::error::#$pr: merge commit $oid message is not the single subject line"
    status=1
  fi
done
exit "$status"
