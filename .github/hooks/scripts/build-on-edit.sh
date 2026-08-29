#!/usr/bin/env bash
# PostToolUse hook: after an edit, auto-build/test so agents get deterministic
# pass/fail feedback instead of relying on being told to run tests.
# Dormant until a .sln exists (Meshwright's M0 solution isn't scaffolded yet).
set -euo pipefail

repo_root="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
cd "$repo_root"

shopt -s nullglob
solutions=(*.sln)
if [[ ${#solutions[@]} -eq 0 ]]; then
  exit 0
fi

# Best-effort: only act on edit-shaped tool calls if we can tell; otherwise run anyway.
input="$(cat || true)"
tool_name="$(printf '%s' "$input" | jq -r '.tool_name // .toolName // .tool.name // empty' 2>/dev/null || true)"
case "$tool_name" in
  ""|*[Ee]dit*|*[Rr]eplace*|*[Cc]reate*) ;;
  *) exit 0 ;;
esac

log_dir="reports/build-hook"
mkdir -p "$log_dir"
stamp="$(date -u +%Y%m%dT%H%M%SZ)"
log_file="$log_dir/$stamp.log"

{
  echo "## dotnet build"
  dotnet build --nologo -v minimal
} > "$log_file" 2>&1
build_status=$?

if [[ $build_status -ne 0 ]]; then
  jq -n --arg reason "dotnet build failed after this edit. See $log_file" \
    '{decision:"block", reason:$reason}'
  exit 0
fi

{
  echo "## dotnet test"
  dotnet test --nologo -v minimal
} >> "$log_file" 2>&1
test_status=$?

if [[ $test_status -ne 0 ]]; then
  jq -n --arg reason "dotnet test failed after this edit. See $log_file" \
    '{decision:"block", reason:$reason}'
  exit 0
fi

exit 0
