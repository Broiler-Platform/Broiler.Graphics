#!/usr/bin/env bash
# Run after dotnet build Broiler.Graphics.slnx using the same configuration.
# Tests are self-hosted console runners; dotnet test cannot discover them.
# The host's platform suite is built explicitly because the solution excludes it.
set -euo pipefail

cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."

configuration="${1:-${CONFIGURATION:-Release}}"
case "$configuration" in
  Debug|Release) ;;
  *) echo "Unknown configuration '$configuration'; use Debug or Release." >&2; exit 2 ;;
esac

failed=''
run_suite() {
  local name="$1"
  local project="src/tests/$name/$name.csproj"
  echo
  echo "=== $name ($configuration) ==="
  if [[ "${2:-}" == --build ]] && ! dotnet build "$project" -c "$configuration" --nologo; then
    echo "FAIL $name (build)" >&2
    failed="$failed $name"
    return
  fi
  if dotnet run --project "$project" -c "$configuration" --no-build; then
    echo "OK   $name"
  else
    echo "FAIL $name" >&2
    failed="$failed $name"
  fi
}

for suite in \
  Broiler.Graphics.Tests \
  Broiler.Graphics.WebAssembly.Tests \
  Broiler.Graphics.Android.Tests; do
  run_suite "$suite"
done

case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*) run_suite Broiler.Graphics.Windows.Tests --build ;;
  Linux*) run_suite Broiler.Graphics.Linux.Tests --build ;;
  *) echo 'Skipping platform suites: Windows or Linux is required.' ;;
esac

echo
if [ -n "$failed" ]; then
  echo "Failed suites:$failed" >&2
  exit 1
fi
echo 'All suites passed.'
