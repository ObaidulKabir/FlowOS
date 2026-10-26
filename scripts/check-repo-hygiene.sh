#!/usr/bin/env bash
# Repository hygiene check (CR-03). Fails when tracked files include logs, local databases,
# archives, scratch output at the root, or files larger than 5 MB.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

fail=0
report() { echo "::error::$1"; fail=1; }

while IFS= read -r -d '' f; do
  case "$f" in
    *.log|*.db|*.sqlite|*.zip)  report "Tracked log, database or archive: $f" ;;
  esac
  case "$f" in
    */*) ;;  # only root-level names are checked below
    tmp*|temp*|test_output*|build_*.txt|*.ps1) report "Scratch file at repository root: $f (move it to scripts/, samples/ or docs/, or delete it)" ;;
  esac
  if [ -f "$f" ] && [ "$(wc -c < "$f")" -gt 5242880 ]; then
    report "File larger than 5 MB: $f"
  fi
done < <(git ls-files -z)

if [ "$fail" -ne 0 ]; then
  echo "Repository hygiene check failed. See CR-03 in the FlowOS Modification Proposal."
  exit 1
fi
echo "Repository hygiene check passed."
