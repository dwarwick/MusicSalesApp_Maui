#!/bin/bash

# Android emulator launch-crash gate.
#
# Companion to test_sims.sh / test_device.sh. Builds the app, then for each
# selected AVD: boots it, installs the APK, launches and force-stops the app N
# times, and scores logcat plus the app's own rolling log for startup crashes.
#
# Why this one carries more weight than the iOS simulator gate: Android Release
# builds use full AOT (RunAOTCompilation=true with AndroidEnableProfiledAot=false,
# see MusicSalesApp.Maui.csproj), and that same AOT code runs on the emulator. So
# unlike the iOS simulator - which JITs, and therefore cannot exercise
# MtouchUseLlvm - this gate runs the real release codegen.
#
# Calibration caveat: Google Play Billing only answers for a build Play
# recognises, installed from a track. A locally-signed build gets errors from
# every billing query, so billing noise is filtered out rather than scored. See
# CLAUDE.md, "Reading device logs", for the incident where that noise produced a
# wrong diagnosis.
#
# Note: /bin/bash on macOS is 3.2 - no `wait -n`, no associative arrays.

set -euo pipefail

WORKSPACE_ROOT="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=test_launch_common.sh
. "$WORKSPACE_ROOT/test_launch_common.sh"

PROJECT_PATH="$WORKSPACE_ROOT/MusicSalesApp.Maui/MusicSalesApp.Maui.csproj"
PACKAGE="net.streamtunes.musicsalesapp.maui"

ANDROID_HOME="${ANDROID_HOME:-${ANDROID_SDK_ROOT:-$HOME/Library/Android/sdk}}"
ADB="$ANDROID_HOME/platform-tools/adb"
EMULATOR="$ANDROID_HOME/emulator/emulator"

BUILD_CONFIGURATION="${BUILD_CONFIGURATION:-Release}"
APP_SETTINGS_ENVIRONMENT="${APP_SETTINGS_ENVIRONMENT:-Test}"
SKIP_BUILD="${SKIP_BUILD:-0}"
TARGET_AVDS="${TARGET_AVDS:-}"
LAUNCH_COUNT="${LAUNCH_COUNT:-3}"
LAUNCH_STABLE_SECONDS="${LAUNCH_STABLE_SECONDS:-10}"
POST_TERMINATE_DELAY_SECONDS="${POST_TERMINATE_DELAY_SECONDS:-2}"
BOOT_TIMEOUT_SECONDS="${BOOT_TIMEOUT_SECONDS:-300}"
KEEP_BOOTED="${KEEP_BOOTED:-0}"
BASE_PORT="${BASE_PORT:-5584}"
RESULTS_ROOT="${RESULTS_ROOT:-$WORKSPACE_ROOT/DeviceLogs/emulator-smoke}"
RETAIN_RUNS="${RETAIN_RUNS:-10}"

usage() {
  cat <<'EOF'
Usage: test_emulators.sh [options]

Builds the Android app, then installs it on each selected AVD and launches /
force-stops it repeatedly, scoring logcat and the app's rolling log for startup
crashes. Exits non-zero if any AVD fails.

Options:
  -h, --help                Show this help.
      --configuration <cfg> Debug|Release                (default: Release)
      --environment <env>   Development|Test|Production  (default: Test)
      --skip-build          Reuse the existing APK.
      --avds "<a>;<b>"      AVD names, ';'-separated     (default: all)
      --launches <n>        Launch/force-stop cycles     (default: 3)
      --stable-seconds <n>  How long a launch must stay up to pass (default: 10)
      --keep-booted         Leave emulators running afterwards (for triage).
      --results-dir <path>  Artifact directory (default: DeviceLogs/emulator-smoke)

Emulators run one at a time and headless. Release builds use full AOT, so the
build takes a few minutes.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help) usage; exit 0 ;;
    --configuration) BUILD_CONFIGURATION="$2"; shift 2 ;;
    --environment) APP_SETTINGS_ENVIRONMENT="$2"; shift 2 ;;
    --skip-build) SKIP_BUILD=1; shift ;;
    --avds) TARGET_AVDS="$2"; shift 2 ;;
    --launches) LAUNCH_COUNT="$2"; shift 2 ;;
    --stable-seconds) LAUNCH_STABLE_SECONDS="$2"; shift 2 ;;
    --keep-booted) KEEP_BOOTED=1; shift ;;
    --results-dir) RESULTS_ROOT="$2"; shift 2 ;;
    *) printf 'Unknown option: %s\n\n' "$1" >&2; usage >&2; exit 1 ;;
  esac
done

APK_PATH="$WORKSPACE_ROOT/MusicSalesApp.Maui/bin/$BUILD_CONFIGURATION/net10.0-android/$PACKAGE-Signed.apk"

RUN_STAMP="$(date +%Y%m%d-%H%M%S)"
RUN_DIR="$RESULTS_ROOT/$RUN_STAMP"
AVDS_DIR="$RUN_DIR/avds"
mkdir -p "$AVDS_DIR"
rm -f "$RESULTS_ROOT/latest"
ln -s "$RUN_STAMP" "$RESULTS_ROOT/latest" 2>/dev/null || true

RUNNING_SERIAL=""
RUNNING_PID=""

cleanup() {
  local rc=$?
  trap - EXIT INT TERM
  if [[ "$KEEP_BOOTED" != "1" ]]; then
    stop_emulator
  fi
  exit "$rc"
}

stop_emulator() {
  if [[ -n "$RUNNING_SERIAL" ]]; then
    "$ADB" -s "$RUNNING_SERIAL" emu kill >/dev/null 2>&1 || true
  fi
  if [[ -n "$RUNNING_PID" ]]; then
    kill "$RUNNING_PID" 2>/dev/null || true
    wait "$RUNNING_PID" 2>/dev/null || true
  fi
  RUNNING_SERIAL=""
  RUNNING_PID=""
}

trap cleanup EXIT INT TERM

build_apk() {
  printf 'Building the Android app...\n'
  printf '  Configuration:          %s\n' "$BUILD_CONFIGURATION"
  printf '  AppSettingsEnvironment: %s\n' "$APP_SETTINGS_ENVIRONMENT"
  printf '  (Release uses full AOT, so this takes a few minutes.)\n'

  dotnet build "$PROJECT_PATH" \
    -f net10.0-android \
    -c "$BUILD_CONFIGURATION" \
    -p:AppSettingsEnvironment="$APP_SETTINGS_ENVIRONMENT"
}

list_avds() {
  "$EMULATOR" -list-avds 2>/dev/null | sed '/^$/d'
}

should_test_avd() {
  local name="$1"

  [[ -n "$TARGET_AVDS" ]] || return 0

  local requested requested_list
  IFS=';' read -r -a requested_list <<< "$TARGET_AVDS"
  for requested in "${requested_list[@]}"; do
    [[ "$name" == "$requested" ]] && return 0
  done
  return 1
}

# Boots an AVD headless on its own port and waits for it to finish booting.
start_emulator() {
  local avd="$1" port="$2"
  local serial="emulator-$port"

  "$EMULATOR" -avd "$avd" -port "$port" \
    -no-window -no-audio -no-boot-anim -no-snapshot -wipe-data \
    >"$AVDS_DIR/$(slug "$avd")/emulator.log" 2>&1 &
  RUNNING_PID=$!
  RUNNING_SERIAL="$serial"

  local deadline=$((SECONDS + BOOT_TIMEOUT_SECONDS))
  local booted=""
  while [[ "$SECONDS" -lt "$deadline" ]]; do
    booted="$("$ADB" -s "$serial" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r\n' || true)"
    [[ "$booted" == "1" ]] && break
    sleep 3
  done

  if [[ "$booted" != "1" ]]; then
    printf 'Emulator %s did not finish booting within %ss.\n' "$avd" "$BOOT_TIMEOUT_SECONDS" >&2
    return 1
  fi

  # Settle: the launcher and package manager need a moment after boot_completed
  # or the first `am start` races with PackageManager.
  "$ADB" -s "$serial" shell input keyevent 82 >/dev/null 2>&1 || true
  sleep 3
  return 0
}

# Resolves the launcher activity; `cmd package` does not exist on older API
# levels, so fall back to monkey's implicit launcher intent.
launcher_component() {
  local serial="$1" component

  component="$("$ADB" -s "$serial" shell cmd package resolve-activity --brief "$PACKAGE" 2>/dev/null \
                | tr -d '\r' | tail -1 || true)"

  if [[ "$component" == */* && "$component" != *"No activity found"* ]]; then
    printf '%s\n' "$component"
  fi
}

app_pid() {
  local serial="$1" pid

  pid="$("$ADB" -s "$serial" shell pidof "$PACKAGE" 2>/dev/null | tr -d '\r\n' || true)"
  if [[ -z "$pid" ]]; then
    pid="$("$ADB" -s "$serial" shell ps -A 2>/dev/null | tr -d '\r' \
            | awk -v p="$PACKAGE" '$NF == p { print $2; exit }' || true)"
  fi
  printf '%s\n' "$pid"
}

# 0 = stayed up for the stability window, 1 = died (or never started).
# Publishes the app's pid in LAUNCHED_PID so the caller can scope logcat to it.
launch_once() {
  local serial="$1" out="$2" component="$3"

  LAUNCHED_PID=""
  "$ADB" -s "$serial" logcat -c >/dev/null 2>&1 || true

  if [[ -n "$component" ]]; then
    "$ADB" -s "$serial" shell am start -W -n "$component" >"$out" 2>&1 || true
  else
    "$ADB" -s "$serial" shell monkey -p "$PACKAGE" -c android.intent.category.LAUNCHER 1 \
      >"$out" 2>&1 || true
  fi

  local deadline=$((SECONDS + LAUNCH_STABLE_SECONDS))
  local pid
  pid="$(app_pid "$serial")"
  if [[ -z "$pid" ]]; then
    printf '\nApp process never appeared after the start intent.\n' >>"$out"
    return 1
  fi
  LAUNCHED_PID="$pid"

  while [[ "$SECONDS" -lt "$deadline" ]]; do
    if [[ "$(app_pid "$serial")" != "$pid" ]]; then
      printf '\nApp process %s disappeared before the stability window elapsed.\n' "$pid" >>"$out"
      return 1
    fi
    sleep 0.5
  done

  return 0
}

test_avd() {
  local avd="$1" port="$2" dir="$3"
  local serial="emulator-$port"
  local findings="$dir/findings.txt"
  local package_death_re
  # shellcheck disable=SC2059
  package_death_re="$(printf "$ANDROID_PACKAGE_DEATH_TEMPLATE" \
                        "$PACKAGE" "$PACKAGE" "$PACKAGE")"

  printf '===========================================\n'
  printf 'Testing %s\n' "$avd"
  printf '===========================================\n'

  start_emulator "$avd" "$port"

  printf 'Installing %s...\n' "${APK_PATH##*/}"
  "$ADB" -s "$serial" install -r -t "$APK_PATH"

  local component
  component="$(launcher_component "$serial")"
  printf 'Launcher activity: %s\n' "${component:-<monkey fallback>}"

  local i out hits
  for ((i = 1; i <= LAUNCH_COUNT; i++)); do
    out="$dir/launch-$i.start.log"
    launches=$((launches + 1))

    printf '>>> Launch %s of %s\n' "$i" "$LAUNCH_COUNT"

    local alive=0
    launch_once "$serial" "$out" "$component" || alive=1

    # Capture logs before force-stopping. The full system log is kept for
    # context but scored only for lines naming our package - scoring it wholesale
    # matches ActivityManager reaping unrelated system processes constantly.
    "$ADB" -s "$serial" logcat -d >"$dir/launch-$i.logcat.log" 2>/dev/null || true
    "$ADB" -s "$serial" logcat -b crash -d >"$dir/launch-$i.crashbuf.log" 2>/dev/null || true
    if [[ -n "$LAUNCHED_PID" ]]; then
      "$ADB" -s "$serial" logcat -d --pid="$LAUNCHED_PID" \
        >"$dir/launch-$i.applog.log" 2>/dev/null || true
    fi

    if [[ "$alive" == "0" ]]; then
      printf 'PASS: stayed up for %ss, then force-stopped.\n' "$LAUNCH_STABLE_SECONDS"
    else
      printf 'FAIL: app did not stay up for %ss (see %s).\n' "$LAUNCH_STABLE_SECONDS" "${out##*/}"
      failures=$((failures + 1))
      reasons="$reasons launch-$i-died;"
    fi

    # Everything in the pid-scoped log is ours by construction.
    hits="$(scan_log "$dir/launch-$i.applog.log" "$ANDROID_CRASH_RE" \
              "launch-$i process log" "$findings" "$ANDROID_EXCLUDE_RE")"
    if [[ "$hits" != "0" ]]; then
      printf 'FAIL: %s crash marker(s) in the launch-%s process log.\n' "$hits" "$i"
      failures=$((failures + 1))
      reasons="$reasons launch-$i-applog($hits);"
    fi

    hits="$(scan_log "$dir/launch-$i.logcat.log" "$package_death_re" \
              "launch-$i system log (our package)" "$findings" "$ANDROID_EXCLUDE_RE")"
    if [[ "$hits" != "0" ]]; then
      printf 'FAIL: %s death/ANR record(s) for our package in launch-%s.\n' "$hits" "$i"
      failures=$((failures + 1))
      reasons="$reasons launch-$i-syslog($hits);"
    fi

    # The crash buffer holds crashes from every app, so it only counts when it
    # names ours.
    if grep -q "$PACKAGE" "$dir/launch-$i.crashbuf.log" 2>/dev/null; then
      hits="$(scan_log "$dir/launch-$i.crashbuf.log" "$ANDROID_CRASH_RE" \
                "launch-$i crash buffer" "$findings" "$ANDROID_EXCLUDE_RE")"
      if [[ "$hits" != "0" ]]; then
        printf 'FAIL: %s entr(ies) in the launch-%s crash buffer.\n' "$hits" "$i"
        failures=$((failures + 1))
        reasons="$reasons launch-$i-crashbuf($hits);"
      fi
    fi

    "$ADB" -s "$serial" shell am force-stop "$PACKAGE" >/dev/null 2>&1 || true
    sleep "$POST_TERMINATE_DELAY_SECONDS"
  done

  # The app's own rolling log lives on external storage on Android.
  if "$ADB" -s "$serial" pull "/sdcard/Android/data/$PACKAGE/files/logs" "$dir/app-logs" \
       >/dev/null 2>&1; then
    local app_log
    shopt -s nullglob
    for app_log in "$dir/app-logs"/streamtunes-*.log; do
      hits="$(scan_log "$app_log" "$APP_LOG_RE" "app log ${app_log##*/}" "$findings" "$ANDROID_EXCLUDE_RE")"
      if [[ "$hits" != "0" ]]; then
        printf 'FAIL: %s error/crash marker(s) in %s.\n' "$hits" "${app_log##*/}"
        failures=$((failures + 1))
        reasons="$reasons app-log($hits);"
      fi
    done
    shopt -u nullglob
  else
    printf 'WARN: could not pull the app log from external storage.\n'
    reasons="$reasons no-app-log;"
  fi

  if [[ "$KEEP_BOOTED" != "1" ]]; then
    printf 'Shutting down %s...\n' "$avd"
    stop_emulator
  fi
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

for tool in "$ADB" "$EMULATOR"; do
  if [[ ! -x "$tool" ]]; then
    printf 'Not found: %s (set ANDROID_HOME)\n' "$tool" >&2
    exit 1
  fi
done

if [[ "$SKIP_BUILD" == "1" ]]; then
  printf 'Skipping build; reusing the existing APK.\n'
else
  build_apk
fi

if [[ ! -f "$APK_PATH" ]]; then
  printf 'APK not found: %s\n' "$APK_PATH" >&2
  exit 1
fi

"$ADB" start-server >/dev/null 2>&1 || true

avds=()
while IFS= read -r avd; do
  [[ -n "$avd" ]] || continue
  should_test_avd "$avd" || continue
  avds+=("$avd")
done < <(list_avds)

if [[ "${#avds[@]}" -eq 0 ]]; then
  printf 'No matching AVDs were found. Create one with avdmanager.\n' >&2
  exit 1
fi

printf '\nTesting %s AVD(s), %s launch(es) each.\n' "${#avds[@]}" "$LAUNCH_COUNT"
printf 'Artifacts: %s\n\n' "$RUN_DIR"

port="$BASE_PORT"
for avd in "${avds[@]}"; do
  dir="$AVDS_DIR/$(slug "$avd")"
  mkdir -p "$dir"

  (
    set -eo pipefail
    failures=0
    launches=0
    reasons=""
    trap 'rc=$?; st=PASS
          if [ "$rc" -ne 0 ]; then st=ERROR; failures=$((failures + 1)); reasons="$reasons harness-exit-$rc;"
          elif [ "$failures" -gt 0 ]; then st=FAIL; fi
          write_result "'"$dir"'" "$st" "$failures" "$launches" "$reasons"' EXIT
    test_avd "$avd" "$port" "$dir"
  ) </dev/null 2>&1 | tee "$dir/console.log" | sed "s/^/[$avd] /" || true

  if [[ ! -f "$dir/result.env" ]]; then
    write_result "$dir" ERROR 1 0 "harness aborted before reporting"
  fi

  # The subshell cannot clear the parent's handle on the emulator it started, so
  # make sure nothing is left running before the next AVD takes the port.
  if [[ "$KEEP_BOOTED" != "1" ]]; then
    "$ADB" -s "emulator-$port" emu kill >/dev/null 2>&1 || true
    RUNNING_SERIAL=""
    RUNNING_PID=""
  fi

  port=$((port + 2))
done

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------

total_avds=0
total_launches=0
total_failures=0
failed_avds=0

{
  printf 'Android emulator launch-crash gate\n'
  printf 'Run:           %s\n' "$RUN_STAMP"
  printf 'Configuration: %s / %s\n' "$BUILD_CONFIGURATION" "$APP_SETTINGS_ENVIRONMENT"
  printf 'Launches:      %s x %ss\n\n' "$LAUNCH_COUNT" "$LAUNCH_STABLE_SECONDS"
} >"$RUN_DIR/summary.txt"

shopt -s nullglob
for dir in "$AVDS_DIR"/*; do
  [[ -d "$dir" ]] || continue
  status=ERROR; failures=1; launches=0; reasons="missing result.env"
  # shellcheck disable=SC1090
  [[ -f "$dir/result.env" ]] && . "$dir/result.env"

  total_avds=$((total_avds + 1))
  total_launches=$((total_launches + launches))
  total_failures=$((total_failures + failures))
  [[ "$status" == "PASS" ]] || failed_avds=$((failed_avds + 1))

  printf '%-6s %-24s %s\n' "$status" "${dir##*/}" "$reasons" >>"$RUN_DIR/summary.txt"
done
shopt -u nullglob

{
  printf '\nAVDs: %s   Launches: %s   Failures: %s   Failed AVDs: %s\n' \
    "$total_avds" "$total_launches" "$total_failures" "$failed_avds"
  print_gate_notes
  printf '  - Google Play Billing noise is filtered out: Play only answers for a\n'
  printf '    build it recognises, installed from a track, so billing errors on a\n'
  printf '    locally-signed APK are expected and are not scored.\n'
} >>"$RUN_DIR/summary.txt"

printf '\n'
cat "$RUN_DIR/summary.txt"
printf '\nArtifacts: %s\n' "$RUN_DIR"

if [[ "$RETAIN_RUNS" -gt 0 ]]; then
  # BSD head has no negative line count, so drop the newest N and delete the rest.
  ls -1d "$RESULTS_ROOT"/*/ 2>/dev/null | sort -r | tail -n +"$((RETAIN_RUNS + 1))" | while IFS= read -r old; do
    rm -rf "$old"
  done
fi

if [[ "$total_failures" -ne 0 ]]; then
  exit 1
fi
