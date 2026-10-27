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

# CR-05: MediatR 13 and later need a commercial licence. Every MediatR package must stay below 13.
while IFS= read -r line; do
  ver=$(echo "$line" | sed -nE 's/.*Version="([0-9]+)\..*/\1/p')
  if [ -n "$ver" ] && [ "$ver" -ge 13 ]; then
    report "MediatR 13+ is commercially licensed (CR-05): $line"
  fi
done < <(git ls-files -z -- '*.props' '*.csproj' | xargs -0 grep -hE 'Include="MediatR[^"]*"[^>]*Version=' || true)

# CR-08: time-based code should take an injected TimeProvider. Direct clock calls in src/ may only
# go down; lower scripts/clock-usage-baseline.txt when you remove some.
clock_baseline=$(tr -dc '0-9' < scripts/clock-usage-baseline.txt)
clock_count=$(git grep -nE 'DateTime(Offset)?\.(UtcNow|Now)\b' -- 'src/*.cs' ':!src/**/Migrations/**' | wc -l | tr -d ' ')
if [ "$clock_count" -gt "$clock_baseline" ]; then
  report "Direct clock calls in src/ rose from $clock_baseline to $clock_count (CR-08). Inject TimeProvider instead."
elif [ "$clock_count" -lt "$clock_baseline" ]; then
  echo "Direct clock calls fell to $clock_count; you can lower scripts/clock-usage-baseline.txt."
fi

if [ "$fail" -ne 0 ]; then
  echo "Repository hygiene check failed. See CR-03, CR-05 and CR-08 in the FlowOS Modification Proposal."
  exit 1
fi
echo "Repository hygiene check passed."
