#!/bin/bash

# Physical-device launch-crash gate.
#
# Companion to test_sims.sh, and the reason both exist:
#
#   test_sims.sh builds for iossimulator-arm64, which JITs. That exercises
#   MtouchRegistrar=static and MtouchLink=SdkOnly - the mechanisms behind the
#   documented iPadOS 26 App Store launch crash - across every iPhone and iPad
#   form factor, but it does NOT exercise MtouchUseLlvm, because there is no AOT
#   on the simulator.
#
#   This script builds for ios-arm64 in Release, which is the same codegen path
#   (LLVM AOT + static registrar + SdkOnly linking) that produces the IPA App
#   Store review runs. It signs with the Apple Development identity and a
#   development provisioning profile so the result can be installed on a paired
#   device; the codegen is identical to the distribution build, only the
#   signature differs.
#
# Together: test_sims.sh gives breadth (form factors and OS versions we own no
# hardware for, notably iPad), this gives depth (the real release codegen).
#
# Note: /bin/bash on macOS is 3.2 - no `wait -n`, no associative arrays.

set -euo pipefail

WORKSPACE_ROOT="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=test_launch_common.sh
. "$WORKSPACE_ROOT/test_launch_common.sh"

PROJECT_PATH="$WORKSPACE_ROOT/MusicSalesApp.Maui/MusicSalesApp.Maui.csproj"
BUNDLE_ID="net.streamtunes.musicsalesapp.maui"
EXECUTABLE_NAME="MusicSalesApp.Maui"

BUILD_CONFIGURATION="${BUILD_CONFIGURATION:-Release}"
APP_SETTINGS_ENVIRONMENT="${APP_SETTINGS_ENVIRONMENT:-Test}"
RUNTIME_IDENTIFIER="${RUNTIME_IDENTIFIER:-ios-arm64}"
CODESIGN_KEY="${CODESIGN_KEY:-Apple Development: david.warwick1969@gmail.com (26R3K97H8Y)}"
CODESIGN_PROVISION="${CODESIGN_PROVISION:-ProfileIncludingIphone}"
SKIP_BUILD="${SKIP_BUILD:-0}"
LAUNCH_COUNT="${LAUNCH_COUNT:-3}"
LAUNCH_STABLE_SECONDS="${LAUNCH_STABLE_SECONDS:-15}"
POST_TERMINATE_DELAY_SECONDS="${POST_TERMINATE_DELAY_SECONDS:-2}"
DEVICE="${DEVICE:-}"
RESULTS_ROOT="${RESULTS_ROOT:-$WORKSPACE_ROOT/DeviceLogs/device-smoke}"
RETAIN_RUNS="${RETAIN_RUNS:-10}"

usage() {
  cat <<'EOF'
Usage: test_device.sh [options]

Builds the app for a physical iPhone/iPad in Release (LLVM AOT - the same
codegen App Store review runs), installs it on the paired device, and
launches/force-closes it repeatedly, scoring the logs for launch crashes.
Exits non-zero if anything failed.

Options:
  -h, --help                Show this help.
      --configuration <cfg> Debug|Release                (default: Release)
      --environment <env>   Development|Test|Production  (default: Test)
      --skip-build          Reuse the existing .app bundle.
      --launches <n>        Launch/force-close cycles    (default: 3)
      --stable-seconds <n>  How long a launch must stay up to pass (default: 15)
      --device <name>       Device name/UDID (default: the one paired iOS device)
      --results-dir <path>  Artifact directory (default: DeviceLogs/device-smoke)

Run this in addition to test_sims.sh before submitting to App Store review.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help) usage; exit 0 ;;
    --configuration) BUILD_CONFIGURATION="$2"; shift 2 ;;
    --environment) APP_SETTINGS_ENVIRONMENT="$2"; shift 2 ;;
    --skip-build) SKIP_BUILD=1; shift ;;
    --launches) LAUNCH_COUNT="$2"; shift 2 ;;
    --stable-seconds) LAUNCH_STABLE_SECONDS="$2"; shift 2 ;;
    --device) DEVICE="$2"; shift 2 ;;
    --results-dir) RESULTS_ROOT="$2"; shift 2 ;;
    *) printf 'Unknown option: %s\n\n' "$1" >&2; usage >&2; exit 1 ;;
  esac
done

APP_PATH="$WORKSPACE_ROOT/MusicSalesApp.Maui/bin/$BUILD_CONFIGURATION/net10.0-ios/$RUNTIME_IDENTIFIER/$EXECUTABLE_NAME.app"

RUN_STAMP="$(date +%Y%m%d-%H%M%S)"
# Used to scope the pulled app log to this run. Unlike the simulator gate, which
# uninstalls first and so starts from an empty container, installing over an
# existing device app keeps months of prior log history that must not be scored.
RUN_START_ISO="$(date +%Y-%m-%dT%H:%M:%S)"
RUN_DIR="$RESULTS_ROOT/$RUN_STAMP"
TMP_DIR="$RUN_DIR/.tmp"
FINDINGS="$RUN_DIR/findings.txt"
mkdir -p "$TMP_DIR"
rm -f "$RESULTS_ROOT/latest"
ln -s "$RUN_STAMP" "$RESULTS_ROOT/latest" 2>/dev/null || true

failures=0
launches=0
reasons=""

# ---------------------------------------------------------------------------
# Device resolution
# ---------------------------------------------------------------------------

resolve_device() {
  if [[ -n "$DEVICE" ]]; then
    printf '%s\n' "$DEVICE"
    return 0
  fi

  # Same idiom as .vscode/pull-latest-runtime-log-macos.sh: devicectl only
  # supports JSON to a file, so write it out and parse with python3.
  xcrun devicectl list devices --json-output "$TMP_DIR/devices.json" >/dev/null

  python3 - "$TMP_DIR/devices.json" <<'PY'
import json, sys

with open(sys.argv[1], 'r', encoding='utf-8') as handle:
    payload = json.load(handle)

for device in payload.get('result', {}).get('devices', []):
    hardware = device.get('hardwareProperties', {})
    name = device.get('deviceProperties', {}).get('name')
    if hardware.get('platform') == 'iOS' and name:
        print(name)
        break
PY
}

device_pid() {
  local device="$1"

  xcrun devicectl device info processes --device "$device" \
    --json-output "$TMP_DIR/processes.json" >/dev/null 2>&1 || return 1

  python3 - "$TMP_DIR/processes.json" "$EXECUTABLE_NAME" <<'PY'
import json, sys

with open(sys.argv[1], 'r', encoding='utf-8') as handle:
    payload = json.load(handle)

needle = sys.argv[2]
for process in payload.get('result', {}).get('runningProcesses', []):
    executable = process.get('executable') or ''
    if needle in executable:
        print(process.get('processIdentifier'))
        break
PY
}

# ---------------------------------------------------------------------------
# Build / install / launch
# ---------------------------------------------------------------------------

build_app_bundle() {
  printf 'Building for a physical device...\n'
  printf '  Configuration:          %s\n' "$BUILD_CONFIGURATION"
  printf '  AppSettingsEnvironment: %s\n' "$APP_SETTINGS_ENVIRONMENT"
  printf '  RuntimeIdentifier:      %s\n' "$RUNTIME_IDENTIFIER"
  printf '  Signing:                %s / %s\n' "$CODESIGN_KEY" "$CODESIGN_PROVISION"

  dotnet build "$PROJECT_PATH" \
    -f net10.0-ios \
    -c "$BUILD_CONFIGURATION" \
    -p:RuntimeIdentifier="$RUNTIME_IDENTIFIER" \
    -p:AppSettingsEnvironment="$APP_SETTINGS_ENVIRONMENT" \
    -p:CodesignKey="$CODESIGN_KEY" \
    -p:CodesignProvision="$CODESIGN_PROVISION" \
    -p:ValidateXcodeVersion=false
}

# Launch once and report whether the app stayed up.
#   0 = stayed up for LAUNCH_STABLE_SECONDS, 1 = died early.
#
# Same sentinel design as test_sims.sh: `devicectl process launch --console`
# blocks and streams the app's stdout/stderr, which is where Mono prints
# "Unhandled managed exception:" plus the managed stack trace. Liveness is the
# sentinel file, not `kill -0` on the launcher (a just-exited child is a zombie
# and kill -0 succeeds on zombies).
launch_once() {
  local device="$1" out="$2"
  local sentinel="$2.exit"

  rm -f "$out" "$sentinel"

  (
    set +e
    xcrun devicectl device process launch --device "$device" \
      --console --terminate-existing "$BUNDLE_ID" >"$out" 2>&1
    printf '%s\n' "$?" >"$sentinel"
  ) </dev/null &
  local launcher=$!

  local deadline=$((SECONDS + LAUNCH_STABLE_SECONDS))
  while [[ "$SECONDS" -lt "$deadline" ]]; do
    if [[ -f "$sentinel" ]]; then
      wait "$launcher" 2>/dev/null || true
      return 1
    fi
    sleep 0.5
  done

  # Force-close: SIGKILL the app on the device, which is what swiping it away
  # from the app switcher does.
  local pid
  pid="$(device_pid "$device" || true)"
  if [[ -n "$pid" ]]; then
    xcrun devicectl device process signal --device "$device" \
      --pid "$pid" --signal SIGKILL >/dev/null 2>&1 || true
  fi

  local drain=$((SECONDS + 15))
  while [[ "$SECONDS" -lt "$drain" && ! -f "$sentinel" ]]; do
    sleep 0.5
  done
  kill "$launcher" 2>/dev/null || true
  wait "$launcher" 2>/dev/null || true
  return 0
}

# Pull the app's rolling log out of the data container.
pull_app_log() {
  local device="$1" dest="$2" relative

  xcrun devicectl device info files \
    --device "$device" \
    --domain-type appDataContainer \
    --domain-identifier "$BUNDLE_ID" \
    --subdirectory Library/logs \
    --no-recurse \
    --json-output "$TMP_DIR/files.json" >/dev/null 2>&1 || return 1

  relative="$(python3 - "$TMP_DIR/files.json" <<'PY'
import json, sys

with open(sys.argv[1], 'r', encoding='utf-8') as handle:
    payload = json.load(handle)

logs = []
for item in payload.get('result', {}).get('files', []):
    name = item.get('name', '')
    if name.startswith('streamtunes-') and name.endswith('.log'):
        logs.append((name, item.get('relativePath', name)))

if logs:
    print("Library/logs/" + sorted(logs, key=lambda entry: entry[0])[-1][1])
PY
)"

  [[ -n "$relative" ]] || return 1

  xcrun devicectl device copy from \
    --device "$device" \
    --domain-type appDataContainer \
    --domain-identifier "$BUNDLE_ID" \
    --source "$relative" \
    --destination "$dest" >/dev/null 2>&1
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

if [[ "$SKIP_BUILD" == "1" ]]; then
  printf 'Skipping build; reusing the existing app bundle.\n'
else
  build_app_bundle
fi

if [[ ! -d "$APP_PATH" ]]; then
  printf 'App bundle not found: %s\n' "$APP_PATH" >&2
  exit 1
fi

device="$(resolve_device)"
if [[ -z "$device" ]]; then
  printf 'No paired iOS device was found. Connect the iPhone and unlock it.\n' >&2
  exit 1
fi

printf '\nDevice: %s\n' "$device"
printf 'Artifacts: %s\n\n' "$RUN_DIR"

# Distinguishes "we could not run the test" from "the app crashed". Exit code 2
# means blocked; 1 means a genuine launch failure.
write_blocked_summary() {
  {
    printf 'iOS physical-device launch-crash gate\n'
    printf 'Run:           %s\n' "$RUN_STAMP"
    printf 'Device:        %s\n\n' "${device:-unknown}"
    printf 'RESULT: BLOCKED - %s\n' "$1"
    printf '\nThe gate did not run to completion, so this is NOT evidence that the\n'
    printf 'app is healthy. Resolve the blocker and re-run.\n'
  } >"$RUN_DIR/summary.txt"
  cat "$RUN_DIR/summary.txt"
}

printf 'Installing %s...\n' "$APP_PATH"

# Over a Wi-Fi pairing the bundle transfer is reset by the peer often enough that
# a single attempt is not a reliable signal. Retry before concluding anything.
install_attempts=3
attempt=1
installed=0
while [[ "$attempt" -le "$install_attempts" ]]; do
  if xcrun devicectl device install app --device "$device" "$APP_PATH" \
       --json-output "$TMP_DIR/install-$attempt.json" >"$TMP_DIR/install-$attempt.log" 2>&1; then
    installed=1
    break
  fi
  printf 'Install attempt %s of %s failed.\n' "$attempt" "$install_attempts"
  attempt=$((attempt + 1))
  [[ "$attempt" -le "$install_attempts" ]] && sleep 5
done

if [[ "$installed" != "1" ]]; then
  printf '\nCould not install on %s after %s attempts.\n' "$device" "$install_attempts" >&2
  tail -20 "$TMP_DIR/install-$install_attempts.log" >&2 2>/dev/null || true
  if grep -qiE "Connection reset by peer|could not be established|ControlChannelConnectionError" \
       "$TMP_DIR"/install-*.log 2>/dev/null; then
    printf '\nThis looks like the Wi-Fi pairing dropping the transfer rather than an app\n' >&2
    printf 'problem. Connect the iPhone over USB, unlock it, and re-run.\n' >&2
  fi
  exit 1
fi

for ((i = 1; i <= LAUNCH_COUNT; i++)); do
  out="$RUN_DIR/launch-$i.stdio.log"
  launches=$((launches + 1))

  printf '>>> Launch %s of %s\n' "$i" "$LAUNCH_COUNT"

  alive=0
  launch_once "$device" "$out" || alive=1

  # A locked device refuses the launch outright. That is an environment blocker,
  # not a crash, and must not be reported as one - the whole value of this gate
  # is that a FAIL means "the app died", so anything else has to be separated out.
  if grep -qiE "could not be, unlocked|BSErrorCodeDescription = Locked" "$out" 2>/dev/null; then
    printf '\nBLOCKED: the iPhone is locked, so iOS refused to launch the app.\n' >&2
    printf 'Unlock the device (and keep it awake) and re-run:\n' >&2
    printf '  ./test_device.sh --skip-build --environment %s\n' "$APP_SETTINGS_ENVIRONMENT" >&2
    write_blocked_summary "device locked - launch refused by SpringBoard"
    exit 2
  fi

  if [[ "$alive" == "0" ]]; then
    printf 'PASS: stayed up for %ss, then force-closed.\n' "$LAUNCH_STABLE_SECONDS"
  else
    printf 'FAIL: app did not stay up for %ss (see %s).\n' "$LAUNCH_STABLE_SECONDS" "${out##*/}"
    failures=$((failures + 1))
    reasons="$reasons launch-$i-died;"
  fi

  hits="$(scan_log "$out" "$CRASH_RE" "launch-$i stdout/stderr" "$FINDINGS")"
  if [[ "$hits" != "0" ]]; then
    printf 'FAIL: %s crash marker(s) in launch-%s stdout/stderr.\n' "$hits" "$i"
    failures=$((failures + 1))
    reasons="$reasons launch-$i-crash-markers($hits);"
  fi

  sleep "$POST_TERMINATE_DELAY_SECONDS"
done

if pull_app_log "$device" "$RUN_DIR/app-streamtunes-device.log"; then
  # Scope to this run. The log is ISO-8601-prefixed and local-time, so a plain
  # string comparison on the first field orders correctly.
  awk -v start="$RUN_START_ISO" '$1 >= start' \
    "$RUN_DIR/app-streamtunes-device.log" >"$RUN_DIR/app-log-this-run.log" 2>/dev/null || true

  if [[ ! -s "$RUN_DIR/app-log-this-run.log" ]]; then
    printf 'WARN: the app log has no entries from this run.\n'
    reasons="$reasons no-app-log-this-run;"
  fi

  hits="$(scan_log "$RUN_DIR/app-log-this-run.log" "$APP_LOG_RE" "app log (this run)" "$FINDINGS")"
  if [[ "$hits" != "0" ]]; then
    printf 'FAIL: %s error/crash marker(s) in the app log.\n' "$hits"
    failures=$((failures + 1))
    reasons="$reasons app-log($hits);"
  fi
else
  printf 'WARN: could not pull the app log from the data container.\n'
  reasons="$reasons no-app-log;"
fi

{
  printf 'iOS physical-device launch-crash gate\n'
  printf 'Run:           %s\n' "$RUN_STAMP"
  printf 'Device:        %s\n' "$device"
  printf 'Configuration: %s / %s / %s\n' "$BUILD_CONFIGURATION" "$APP_SETTINGS_ENVIRONMENT" "$RUNTIME_IDENTIFIER"
  printf 'Launches:      %s x %ss\n\n' "$LAUNCH_COUNT" "$LAUNCH_STABLE_SECONDS"
  if [[ "$failures" -eq 0 ]]; then
    printf 'RESULT: PASS\n'
  else
    printf 'RESULT: FAIL (%s issue(s)): %s\n' "$failures" "$reasons"
  fi
  print_gate_notes
  printf '  - This gate exercises LLVM AOT codegen, which the simulator gate cannot.\n'
  printf '    It covers only this one device model; run test_sims.sh for form-factor\n'
  printf '    breadth, especially iPad.\n'
} >"$RUN_DIR/summary.txt"

printf '\n'
cat "$RUN_DIR/summary.txt"
printf '\nArtifacts: %s\n' "$RUN_DIR"

if [[ "$RETAIN_RUNS" -gt 0 ]]; then
  # BSD head has no negative line count, so drop the newest N and delete the rest.
  ls -1d "$RESULTS_ROOT"/*/ 2>/dev/null | sort -r | tail -n +"$((RETAIN_RUNS + 1))" | while IFS= read -r old; do
    rm -rf "$old"
  done
fi

if [[ "$failures" -ne 0 ]]; then
  exit 1
fi
