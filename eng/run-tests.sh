#!/usr/bin/env bash
#
# Runs the Broiler.Graphics suites for one solution configuration.
#
# The suites are self-hosted console runners, not a test framework, so there is
# nothing for `dotnet test` to discover. Each runner prints PASS/FAIL per case
# and returns a non-zero exit code when a case fails.
#
# Which runners apply depends on the configuration, because the platform suffix
# decides which backend the solution builds:
#
#   *-Windows        Direct2D runner (net10.0-windows, win-x64) + the neutral three
#   *-Linux          Linux runner    (net10.0, linux-x64)       + the neutral three
#   Debug / Release  the neutral three only
#
# The neutral three - core, WebAssembly, and Android - declare only Debug and
# Release, and the solution maps the suffixed configurations onto those. That is
# why they start with the base configuration rather than the suffixed one.
#
# Usage: eng/run-tests.sh [configuration]
#
# Run it after `dotnet build Broiler.Graphics.slnx -c <configuration>`. The
# runners start with --no-build, so they exercise exactly the binaries that
# build produced, including any -p:VersionSuffix the release workflow passed in.

set -euo pipefail

configuration="${1:-${CONFIGURATION:-}}"

if [ -z "$configuration" ]; then
  case "$(uname -s)" in
    Linux) configuration='Release-Linux' ;;
    *)     configuration='Release-Windows' ;;
  esac
  echo "No configuration given; assuming $configuration on $(uname -s)."
fi

base="${configuration%-Windows}"
base="${base%-Linux}"

if [ "$base" != 'Debug' ] && [ "$base" != 'Release' ]; then
  echo "Unknown configuration '$configuration'." >&2
  exit 2
fi

failed=''

run_suite() {
  local name="$1"
  local project="$2"
  local suite_configuration="$3"

  echo
  echo "=== $name suite ($suite_configuration) ==="
  if dotnet run --project "$project" -c "$suite_configuration" --no-build; then
    echo "OK   $name"
  else
    echo "FAIL $name" >&2
    failed="$failed $name"
  fi
}

case "$configuration" in
  *-Windows)
    run_suite 'direct2d' \
      'src/tests/Broiler.Graphics.Windows.Tests/Broiler.Graphics.Windows.Tests.csproj' \
      "$configuration"
    ;;
  *-Linux)
    run_suite 'linux' \
      'src/tests/Broiler.Graphics.Linux.Tests/Broiler.Graphics.Linux.Tests.csproj' \
      "$configuration"
    ;;
esac

run_suite 'core' \
  'src/tests/Broiler.Graphics.Tests/Broiler.Graphics.Tests.csproj' \
  "$base"

run_suite 'webassembly' \
  'src/tests/Broiler.Graphics.WebAssembly.Tests/Broiler.Graphics.WebAssembly.Tests.csproj' \
  "$base"

run_suite 'android' \
  'src/tests/Broiler.Graphics.Android.Tests/Broiler.Graphics.Android.Tests.csproj' \
  "$base"

echo
if [ -n "$failed" ]; then
  echo "Failed suites:$failed" >&2
  exit 1
fi

echo 'All suites passed.'
