#!/bin/bash

# iOS simulator launch-crash gate.
#
# Builds the app for the iOS simulator, then for every selected simulator:
# installs it, launches and force-closes it N times (default 3), and scores the
# resulting logs for any sign of a launch crash. Intended to be run before
# publishing to TestFlight - see .vscode/publish-and-upload-ios-appstore-macos.sh,
# which invokes it as a gate.
#
# What this does and does not cover:
#   The Release + net10.0-ios PropertyGroup in MusicSalesApp.Maui.csproj keys on
#   the target framework, not the runtime identifier, so MtouchRegistrar=static
#   and MtouchLink=SdkOnly - the settings behind the documented iPadOS 26 launch
#   crash - ARE exercised by a Release simulator build. Simulator builds JIT,
#   though, so MtouchUseLlvm is inert here. This gate is a strong proxy for that
#   class of launch crash; it is not a substitute for smoke-testing the actual
#   TestFlight build on a device.
#
# Note: /bin/bash on macOS is 3.2 - no `wait -n`, no associative arrays.

set -euo pipefail

WORKSPACE_ROOT="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=test_launch_common.sh
. "$WORKSPACE_ROOT/test_launch_common.sh"

PROJECT_PATH="$WORKSPACE_ROOT/MusicSalesApp.Maui/MusicSalesApp.Maui.csproj"
BUNDLE_ID="net.streamtunes.musicsalesapp.maui"
EXECUTABLE_NAME="MusicSalesApp.Maui"
HOST_CRASH_LOG_ROOT="$HOME/Library/Logs/DiagnosticReports"
CORE_SIMULATOR_LOG_ROOT="$HOME/Library/Logs/CoreSimulator"

# The "quick" profile: one phone, one large tablet, one non-Pro tablet. Crossed
# with every installed runtime, that covers the phone/tablet x OS-version axis
# the iPadOS 26 launch crash lived on.
QUICK_DEVICE_NAMES='iPhone 17 Pro;iPad Pro 13-inch (M5);iPad (A16)'

# Existing environment variables are honoured as defaults so anything already
# calling this script keeps working; command-line flags override them.
BUILD_CONFIGURATION="${BUILD_CONFIGURATION:-Release}"
APP_SETTINGS_ENVIRONMENT="${APP_SETTINGS_ENVIRONMENT:-Test}"
RUNTIME_IDENTIFIER="${RUNTIME_IDENTIFIER:-iossimulator-arm64}"
SKIP_BUILD="${SKIP_BUILD:-0}"
TARGET_DEVICE_NAMES="${TARGET_DEVICE_NAMES:-}"
TARGET_DEVICE_REGEX="${TARGET_DEVICE_REGEX:-}"
TARGET_RUNTIME="${TARGET_RUNTIME:-all}"
PROFILE="${PROFILE:-quick}"
LAUNCH_COUNT="${LAUNCH_COUNT:-3}"
LAUNCH_STABLE_SECONDS="${LAUNCH_STABLE_SECONDS:-10}"
POST_TERMINATE_DELAY_SECONDS="${POST_TERMINATE_DELAY_SECONDS:-1}"
JOBS="${JOBS:-2}"
PROBE="${PROBE:-console}"
KEEP_BOOTED="${KEEP_BOOTED:-0}"
RESULTS_ROOT="${RESULTS_ROOT:-$WORKSPACE_ROOT/DeviceLogs/simulator-smoke}"
BOOT_TIMEOUT_SECONDS="${BOOT_TIMEOUT_SECONDS:-180}"
RETAIN_RUNS="${RETAIN_RUNS:-10}"

usage() {
  cat <<'EOF'
Usage: test_sims.sh [options]

Builds the app for the iOS simulator, then installs it on each selected
simulator and launches/force-closes it repeatedly, scoring the logs for
launch crashes. Exits non-zero if any device fails.

Options:
  -h, --help                    Show this help.
      --configuration <cfg>     Debug|Release                (default: Release)
      --environment <env>       Development|Test|Production  (default: Test)
      --rid <rid>               Runtime identifier           (default: iossimulator-arm64)
      --skip-build              Reuse the existing .app bundle.
      --profile <quick|full>    quick = 1 phone + 2 tablets per runtime,
                                full  = every available iPhone/iPad
                                                             (default: quick)
      --devices "<a>;<b>"       Exact device names, ';'-separated. Overrides --profile.
      --device-regex <regex>    Filter device names by regex. Overrides --profile.
      --runtime <ver|all>       Restrict to a runtime, e.g. 26.5 (default: all)
      --launches <n>            Launch/terminate cycles per device (default: 3)
      --stable-seconds <n>      How long a launch must stay up to pass (default: 10)
      --jobs <n>                Devices tested concurrently (default: 2)
      --probe <console|pid>     Liveness mechanism (default: console)
      --keep-booted             Do not shut down simulators afterwards (for triage).
      --results-dir <path>      Where to write artifacts
                                (default: DeviceLogs/simulator-smoke)

Artifacts, including the app's stdout/stderr for every launch, are written to
<results-dir>/<timestamp>/ with a 'latest' symlink. Read summary.txt first,
then devices/<runtime>_<name>/launch-N.stdio.log.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help) usage; exit 0 ;;
    --configuration) BUILD_CONFIGURATION="$2"; shift 2 ;;
    --environment) APP_SETTINGS_ENVIRONMENT="$2"; shift 2 ;;
    --rid) RUNTIME_IDENTIFIER="$2"; shift 2 ;;
    --skip-build) SKIP_BUILD=1; shift ;;
    --profile) PROFILE="$2"; shift 2 ;;
    --devices) TARGET_DEVICE_NAMES="$2"; shift 2 ;;
    --device-regex) TARGET_DEVICE_REGEX="$2"; shift 2 ;;
    --runtime) TARGET_RUNTIME="$2"; shift 2 ;;
    --launches) LAUNCH_COUNT="$2"; shift 2 ;;
    --stable-seconds) LAUNCH_STABLE_SECONDS="$2"; shift 2 ;;
    --jobs) JOBS="$2"; shift 2 ;;
    --probe) PROBE="$2"; shift 2 ;;
    --keep-booted) KEEP_BOOTED=1; shift ;;
    --results-dir) RESULTS_ROOT="$2"; shift 2 ;;
    *) printf 'Unknown option: %s\n\n' "$1" >&2; usage >&2; exit 1 ;;
  esac
done

case "$PROFILE" in
  quick|full) ;;
  *) printf 'Invalid --profile: %s (expected quick or full)\n' "$PROFILE" >&2; exit 1 ;;
esac

case "$PROBE" in
  console|pid) ;;
  *) printf 'Invalid --probe: %s (expected console or pid)\n' "$PROBE" >&2; exit 1 ;;
esac

# An explicit device selection always wins over the profile.
if [[ -z "$TARGET_DEVICE_NAMES" && -z "$TARGET_DEVICE_REGEX" && "$PROFILE" == "quick" ]]; then
  TARGET_DEVICE_NAMES="$QUICK_DEVICE_NAMES"
fi

APP_PATH="$WORKSPACE_ROOT/MusicSalesApp.Maui/bin/$BUILD_CONFIGURATION/net10.0-ios/$RUNTIME_IDENTIFIER/$EXECUTABLE_NAME.app"

RUN_STAMP="$(date +%Y%m%d-%H%M%S)"
RUN_DIR="$RESULTS_ROOT/$RUN_STAMP"
BOOTED_DIR="$RUN_DIR/booted"
DEVICES_DIR="$RUN_DIR/devices"
mkdir -p "$BOOTED_DIR" "$DEVICES_DIR"
touch "$RUN_DIR/run.marker"
rm -f "$RESULTS_ROOT/latest"
ln -s "$RUN_STAMP" "$RESULTS_ROOT/latest" 2>/dev/null || true

# ---------------------------------------------------------------------------
# Cleanup: shut down every simulator this run booted.
# ---------------------------------------------------------------------------

cleanup() {
  local rc=$?
  trap - EXIT INT TERM

  local pid
  for pid in $(jobs -pr 2>/dev/null); do
    kill "$pid" 2>/dev/null || true
  done
  wait 2>/dev/null || true

  if [[ "$KEEP_BOOTED" != "1" ]]; then
    local marker udid
    shopt -s nullglob
    for marker in "$BOOTED_DIR"/*; do
      udid="${marker##*/}"
      printf 'Shutting down %s\n' "$udid" >&2
      xcrun simctl shutdown "$udid" >/dev/null 2>&1 || true
      rm -f "$marker"
    done
    shopt -u nullglob
  fi

  exit "$rc"
}
trap cleanup EXIT INT TERM

# ---------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------

build_app_bundle() {
  printf 'Building iOS simulator app...\n'
  printf '  Configuration:          %s\n' "$BUILD_CONFIGURATION"
  printf '  AppSettingsEnvironment: %s\n' "$APP_SETTINGS_ENVIRONMENT"
  printf '  RuntimeIdentifier:      %s\n' "$RUNTIME_IDENTIFIER"

  dotnet build "$PROJECT_PATH" \
    -f net10.0-ios \
    -c "$BUILD_CONFIGURATION" \
    -p:RuntimeIdentifier="$RUNTIME_IDENTIFIER" \
    -p:AppSettingsEnvironment="$APP_SETTINGS_ENVIRONMENT" \
    -p:ValidateXcodeVersion=false
}

# ---------------------------------------------------------------------------
# Device enumeration
# ---------------------------------------------------------------------------

# Emits "name<TAB>udid<TAB>state<TAB>runtime". The runtime matters: every device
# name exists on both installed runtimes, so a name alone is ambiguous.
list_target_devices() {
  xcrun simctl list devices available --json | /usr/bin/jq -r '
    .devices | to_entries[]
    | (.key
       | capture("SimRuntime\\.(?<os>[A-Za-z]+)-(?<v>[0-9-]+)$")
       | "\(.os)-\(.v | gsub("-";"."))") as $rt
    | .value[]
    | select(.isAvailable)
    | select(.name | test("^(iPhone|iPad)"))
    | [.name, .udid, .state, $rt] | @tsv'
}

should_test_device() {
  local name="$1" runtime="$2"

  if [[ "$TARGET_RUNTIME" != "all" && "$runtime" != *"$TARGET_RUNTIME"* ]]; then
    return 1
  fi

  if [[ -n "$TARGET_DEVICE_NAMES" ]]; then
    local requested_name requested_names
    IFS=';' read -r -a requested_names <<< "$TARGET_DEVICE_NAMES"
    for requested_name in "${requested_names[@]}"; do
      if [[ "$name" == "$requested_name" ]]; then
        return 0
      fi
    done
    return 1
  fi

  if [[ -n "$TARGET_DEVICE_REGEX" && ! "$name" =~ $TARGET_DEVICE_REGEX ]]; then
    return 1
  fi

  return 0
}

# ---------------------------------------------------------------------------
# Boot / install / launch
# ---------------------------------------------------------------------------

boot_device() {
  local udid="$1" state

  # Plain `list devices` (not `available`) so a device mid-Booting is not
  # misread as shut down.
  state="$(xcrun simctl list devices --json \
            | /usr/bin/jq -r --arg u "$udid" '.devices[][] | select(.udid==$u) | .state')"

  if [[ "$state" == "Booted" ]]; then
    printf 'Already booted (leaving it booted afterwards).\n'
    return 0
  fi

  # Register before booting so the EXIT trap still shuts it down if we are
  # killed mid-boot.
  : > "$BOOTED_DIR/$udid"
  run_with_timeout "$BOOT_TIMEOUT_SECONDS" xcrun simctl bootstatus "$udid" -b
}

# Launch once and report whether the app stayed up.
#   0 = stayed up for LAUNCH_STABLE_SECONDS, 1 = died early.
#
# The app's stdout/stderr is captured via --console-pty. That is where Mono
# prints "Unhandled managed exception:" plus the managed stack trace, which is
# almost always the artifact that ends a crash investigation.
#
# Liveness is signalled by a sentinel file rather than `kill -0` on the launcher
# PID: a child that has just exited is a zombie until bash reaps it, and
# `kill -0` succeeds on zombies.
#
# The previous implementation of this probe used
#   xcrun simctl spawn <udid> launchctl procinfo <pid>
# which cannot work: `launchctl procinfo` requires root ("This subcommand
# requires root privileges: procinfo") and simctl spawn runs as the logged-in
# user, so it returned non-zero for healthy and crashed apps alike and scored
# every launch as a failure. Do not reintroduce it.
launch_once_console() {
  local udid="$1" out="$2"
  local sentinel="$2.exit"

  rm -f "$out" "$sentinel"

  (
    set +e
    xcrun simctl launch --terminate-running-process --console-pty \
      "$udid" "$BUNDLE_ID" >"$out" 2>&1
    printf '%s\n' "$?" >"$sentinel"
  ) </dev/null &
  local launcher=$!

  local deadline=$((SECONDS + LAUNCH_STABLE_SECONDS))
  while [[ "$SECONDS" -lt "$deadline" ]]; do
    if [[ -f "$sentinel" ]]; then
      wait "$launcher" 2>/dev/null || true
      return 1
    fi
    sleep 0.25
  done

  xcrun simctl terminate "$udid" "$BUNDLE_ID" >/dev/null 2>&1 || true

  local drain=$((SECONDS + 10))
  while [[ "$SECONDS" -lt "$drain" && ! -f "$sentinel" ]]; do
    sleep 0.25
  done
  kill "$launcher" 2>/dev/null || true
  wait "$launcher" 2>/dev/null || true
  return 0
}

# Fallback probe. Simulator apps are ordinary host processes and simctl prints
# the host PID, so `ps` on the host is authoritative; `comm` is cross-checked to
# defeat PID reuse.
launch_once_pid() {
  local udid="$1" out="$2"
  local pid

  rm -f "$out"

  pid="$(xcrun simctl launch --terminate-running-process "$udid" "$BUNDLE_ID" 2>&1 \
          | tee "$out" | sed -n 's/.*: *\([0-9][0-9]*\)$/\1/p' | tail -n 1)"

  if [[ -z "$pid" ]]; then
    printf 'Could not parse a PID from simctl launch output.\n' >>"$out"
    return 1
  fi

  local deadline=$((SECONDS + LAUNCH_STABLE_SECONDS))
  while [[ "$SECONDS" -lt "$deadline" ]]; do
    if ! ps -p "$pid" -o comm= 2>/dev/null | grep -q "$EXECUTABLE_NAME"; then
      printf 'Process %s exited before the stability window elapsed.\n' "$pid" >>"$out"
      return 1
    fi
    sleep 0.25
  done

  xcrun simctl terminate "$udid" "$BUNDLE_ID" >/dev/null 2>&1 || true
  return 0
}

# ---------------------------------------------------------------------------
# Log collection and scoring
# ---------------------------------------------------------------------------

# The uninstall before install wipes the data container, so the whole app log
# belongs to this run and needs no time windowing.
pull_app_log() {
  local udid="$1" dir="$2" container f found=0

  container="$(xcrun simctl get_app_container "$udid" "$BUNDLE_ID" data 2>/dev/null || true)"
  if [[ -z "$container" || ! -d "$container/Library/logs" ]]; then
    return 1
  fi

  shopt -s nullglob
  for f in "$container/Library/logs"/streamtunes-*.log; do
    cp "$f" "$dir/app-${f##*/}" 2>/dev/null && found=1
  done
  shopt -u nullglob

  [[ "$found" == "1" ]]
}

# The broad predicate is what catches SpringBoard/runningboardd lines, which are
# how a real crash is told apart from an 0x8badf00d launch-watchdog kill - those
# need completely different fixes.
collect_oslog() {
  local udid="$1" dest="$2" start="$3"

  xcrun simctl spawn "$udid" log show --start "$start" --style compact --predicate \
    'process == "MusicSalesApp.Maui" OR eventMessage CONTAINS[c] "musicsalesapp" OR senderImagePath CONTAINS[c] "MusicSalesApp"' \
    >"$dest" 2>/dev/null \
  || xcrun simctl spawn "$udid" log show --start "$start" --style compact \
       --predicate 'process == "MusicSalesApp.Maui"' >"$dest" 2>/dev/null \
  || true
}

collect_simulator_crash_reports() {
  local udid="$1" dir="$2" marker="$3" src report count=0

  src="$CORE_SIMULATOR_LOG_ROOT/$udid/CrashReporter/DiagnosticLogs"
  [[ -d "$src" ]] || { printf '0\n'; return 0; }

  while IFS= read -r report; do
    [[ -n "$report" ]] || continue
    cp "$report" "$dir/crashreports/" 2>/dev/null || true
    count=$((count + 1))
  done < <(find "$src" -type f -newer "$marker" 2>/dev/null || true)

  printf '%s\n' "$count"
}

# ---------------------------------------------------------------------------
# Per-device test
# ---------------------------------------------------------------------------

# EXIT trap for a device subshell. A non-zero exit means the harness itself
# aborted (boot timeout, install failure) before any launch was scored, which
# must not be reported as a pass.
finish_device() {
  local rc=$?
  local dir="$1"
  local st=PASS

  if [[ "$rc" -ne 0 ]]; then
    st=ERROR
    failures=$((failures + 1))
    reasons="$reasons harness-exit-$rc;"
  elif [[ "$failures" -gt 0 ]]; then
    st=FAIL
  fi

  write_result "$dir" "$st" "$failures" "$launches" "$reasons"
}

test_device() {
  local udid="$1" name="$2" runtime="$3" dir="$4"
  local marker="$dir/start.marker"
  local findings="$dir/findings.txt"
  local start_time

  printf '===========================================\n'
  printf 'Testing %s on %s (%s)\n' "$name" "$runtime" "$udid"
  printf '===========================================\n'

  boot_device "$udid"

  start_time="$(date '+%Y-%m-%d %H:%M:%S')"
  touch "$marker"

  printf 'Uninstalling %s...\n' "$BUNDLE_ID"
  xcrun simctl uninstall "$udid" "$BUNDLE_ID" >/dev/null 2>&1 || true

  printf 'Installing %s...\n' "$APP_PATH"
  xcrun simctl install "$udid" "$APP_PATH"

  local i out hits
  for ((i = 1; i <= LAUNCH_COUNT; i++)); do
    out="$dir/launch-$i.stdio.log"
    launches=$((launches + 1))

    printf '>>> Launch %s of %s\n' "$i" "$LAUNCH_COUNT"

    local alive=0
    if [[ "$PROBE" == "console" ]]; then
      launch_once_console "$udid" "$out" || alive=1
    else
      launch_once_pid "$udid" "$out" || alive=1
    fi

    if [[ "$alive" == "0" ]]; then
      printf 'PASS: stayed up for %ss, then terminated cleanly.\n' "$LAUNCH_STABLE_SECONDS"
    else
      printf 'FAIL: app did not stay up for %ss (see %s).\n' "$LAUNCH_STABLE_SECONDS" "${out##*/}"
      failures=$((failures + 1))
      reasons="$reasons launch-$i-died;"
    fi

    hits="$(scan_log "$out" "$CRASH_RE" "launch-$i stdout/stderr" "$findings")"
    if [[ "$hits" != "0" ]]; then
      printf 'FAIL: %s crash marker(s) in launch-%s stdout/stderr.\n' "$hits" "$i"
      failures=$((failures + 1))
      reasons="$reasons launch-$i-crash-markers($hits);"
    fi

    sleep "$POST_TERMINATE_DELAY_SECONDS"
  done

  # Collect everything before shutting the device down.
  if pull_app_log "$udid" "$dir"; then
    local app_log
    shopt -s nullglob
    for app_log in "$dir"/app-streamtunes-*.log; do
      hits="$(scan_log "$app_log" "$APP_LOG_RE" "app log ${app_log##*/}" "$findings")"
      if [[ "$hits" != "0" ]]; then
        printf 'FAIL: %s error/crash marker(s) in %s.\n' "$hits" "${app_log##*/}"
        failures=$((failures + 1))
        reasons="$reasons app-log($hits);"
      fi
    done
    shopt -u nullglob
  else
    # Not a hard failure - logging init could legitimately be late - but after
    # three launches an empty log directory is worth surfacing.
    printf 'WARN: no app log directory in the data container; the app may have died before logging started.\n'
    reasons="$reasons no-app-log;"
  fi

  collect_oslog "$udid" "$dir/oslog.log" "$start_time"
  hits="$(scan_log "$dir/oslog.log" "$CRASH_RE" "os_log" "$findings")"
  if [[ "$hits" != "0" ]]; then
    printf 'FAIL: %s crash marker(s) in os_log.\n' "$hits"
    failures=$((failures + 1))
    reasons="$reasons oslog($hits);"
  fi

  local crash_count
  crash_count="$(collect_simulator_crash_reports "$udid" "$dir" "$marker")"
  if [[ "$crash_count" != "0" ]]; then
    printf 'FAIL: %s simulator crash report(s).\n' "$crash_count"
    failures=$((failures + 1))
    reasons="$reasons crashreports($crash_count);"
  fi

  if [[ "$KEEP_BOOTED" != "1" && -f "$BOOTED_DIR/$udid" ]]; then
    printf 'Shutting down %s...\n' "$name"
    xcrun simctl shutdown "$udid" >/dev/null 2>&1 || true
    rm -f "$BOOTED_DIR/$udid"
  fi
}

# Each device runs in its own subshell reporting only through result.env, so one
# device blowing up cannot abort the matrix.
run_device_job() {
  local name="$1" udid="$2" runtime="$3" dir="$4"
  local label="$runtime $name"

  set +e
  mkdir -p "$dir/crashreports"

  (
    set -eo pipefail
    failures=0
    launches=0
    reasons=""
    trap 'finish_device "'"$dir"'"' EXIT
    test_device "$udid" "$name" "$runtime" "$dir"
  ) </dev/null >"$dir/console.log" 2>&1

  # Covers a subshell killed outright, which never runs its EXIT trap.
  if [[ ! -f "$dir/result.env" ]]; then
    write_result "$dir" ERROR 1 0 "device harness aborted before reporting"
  fi

  sed "s/^/[$label] /" "$dir/console.log"
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

devices=()
while IFS=$'\t' read -r name udid state runtime; do
  [[ -n "$name" ]] || continue
  should_test_device "$name" "$runtime" || continue
  devices+=("$name"$'\t'"$udid"$'\t'"$runtime")
done < <(list_target_devices)

if [[ "${#devices[@]}" -eq 0 ]]; then
  printf 'No matching iPhone or iPad simulators were found.\n' >&2
  exit 1
fi

printf '\nTesting %s device(s), %s launch(es) each, %s at a time.\n' \
  "${#devices[@]}" "$LAUNCH_COUNT" "$JOBS"
printf 'Artifacts: %s\n\n' "$RUN_DIR"

throttle() {
  while [[ "$(jobs -pr | wc -l | tr -d ' ')" -ge "$JOBS" ]]; do
    sleep 1
  done
}

for entry in "${devices[@]}"; do
  IFS=$'\t' read -r name udid runtime <<< "$entry"
  throttle
  run_device_job "$name" "$udid" "$runtime" "$DEVICES_DIR/$(slug "${runtime}_${name}")" &
done
wait

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

total_devices=0
total_launches=0
total_failures=0
failed_devices=0

{
  printf 'iOS simulator launch-crash gate\n'
  printf 'Run:           %s\n' "$RUN_STAMP"
  printf 'Configuration: %s / %s / %s\n' "$BUILD_CONFIGURATION" "$APP_SETTINGS_ENVIRONMENT" "$RUNTIME_IDENTIFIER"
  printf 'Profile:       %s (%s launches x %ss, probe=%s)\n\n' \
    "$PROFILE" "$LAUNCH_COUNT" "$LAUNCH_STABLE_SECONDS" "$PROBE"
} >"$RUN_DIR/summary.txt"

shopt -s nullglob
for dir in "$DEVICES_DIR"/*; do
  [[ -d "$dir" ]] || continue
  status=ERROR; failures=1; launches=0; reasons="missing result.env"
  # shellcheck disable=SC1090
  [[ -f "$dir/result.env" ]] && . "$dir/result.env"

  total_devices=$((total_devices + 1))
  total_launches=$((total_launches + launches))
  total_failures=$((total_failures + failures))
  [[ "$status" == "PASS" ]] || failed_devices=$((failed_devices + 1))

  printf '%-6s %-40s %s\n' "$status" "${dir##*/}" "$reasons" >>"$RUN_DIR/summary.txt"
done
shopt -u nullglob

# Host-wide crash reports are not attributable to a device once devices run
# concurrently, so they are scored once, for the run as a whole.
host_reports="$(find "$HOST_CRASH_LOG_ROOT" -name "$EXECUTABLE_NAME*" -newer "$RUN_DIR/run.marker" 2>/dev/null || true)"
if [[ -n "$host_reports" ]]; then
  {
    printf '\nHost crash reports (%s):\n' "$HOST_CRASH_LOG_ROOT"
    printf '%s\n' "$host_reports"
  } >>"$RUN_DIR/summary.txt"
  total_failures=$((total_failures + 1))
fi

{
  printf '\nDevices: %s   Launches: %s   Failures: %s   Failed devices: %s\n' \
    "$total_devices" "$total_launches" "$total_failures" "$failed_devices"
  print_gate_notes
  printf '  - A simulator build JITs, so MtouchUseLlvm is not exercised here. Run\n'
  printf '    test_device.sh for the real release codegen on physical hardware.\n'
} >>"$RUN_DIR/summary.txt"

printf '\n'
cat "$RUN_DIR/summary.txt"
printf '\nArtifacts: %s\n' "$RUN_DIR"

# Keep the results directory from growing without bound.
if [[ "$RETAIN_RUNS" -gt 0 ]]; then
  # BSD head has no negative line count, so drop the newest N and delete the rest.
  ls -1d "$RESULTS_ROOT"/*/ 2>/dev/null | sort -r | tail -n +"$((RETAIN_RUNS + 1))" | while IFS= read -r old; do
    rm -rf "$old"
  done
fi

if [[ "$total_failures" -ne 0 ]]; then
  exit 1
fi
