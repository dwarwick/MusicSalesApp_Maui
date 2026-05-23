#!/bin/bash

set -euo pipefail

WORKSPACE_ROOT="$(cd "$(dirname "$0")" && pwd)"
PROJECT_PATH="$WORKSPACE_ROOT/MusicSalesApp.Maui/MusicSalesApp.Maui.csproj"
BUNDLE_ID="net.streamtunes.musicsalesapp.maui"
BUILD_CONFIGURATION="${BUILD_CONFIGURATION:-Release}"
APP_SETTINGS_ENVIRONMENT="${APP_SETTINGS_ENVIRONMENT:-Test}"
RUNTIME_IDENTIFIER="${RUNTIME_IDENTIFIER:-iossimulator-arm64}"
SKIP_BUILD="${SKIP_BUILD:-0}"
TARGET_DEVICE_NAMES="${TARGET_DEVICE_NAMES:-}"
TARGET_DEVICE_REGEX="${TARGET_DEVICE_REGEX:-}"
APP_PATH="$WORKSPACE_ROOT/MusicSalesApp.Maui/bin/$BUILD_CONFIGURATION/net10.0-ios/$RUNTIME_IDENTIFIER/MusicSalesApp.Maui.app"
LAUNCH_COUNT="${LAUNCH_COUNT:-3}"
LAUNCH_WAIT_SECONDS="${LAUNCH_WAIT_SECONDS:-15}"
POST_TERMINATE_DELAY_SECONDS="${POST_TERMINATE_DELAY_SECONDS:-2}"
CRASH_LOG_ROOT="$HOME/Library/Logs/DiagnosticReports"

TOTAL_DEVICES=0
TOTAL_LAUNCHES=0
TOTAL_FAILURES=0

build_app_bundle() {
  echo "Building iOS simulator app..."
  echo "Configuration: $BUILD_CONFIGURATION"
  echo "AppSettingsEnvironment: $APP_SETTINGS_ENVIRONMENT"
  echo "RuntimeIdentifier: $RUNTIME_IDENTIFIER"

  dotnet build "$PROJECT_PATH" \
    -f net10.0-ios \
    -c "$BUILD_CONFIGURATION" \
    -p:RuntimeIdentifier="$RUNTIME_IDENTIFIER" \
    -p:AppSettingsEnvironment="$APP_SETTINGS_ENVIRONMENT"
}

should_test_device() {
  local name="$1"

  if [[ -n "$TARGET_DEVICE_NAMES" ]]; then
    local requested_name
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

list_target_devices() {
  xcrun simctl list devices available |
    sed -n '/== Devices ==/,$p' |
    grep -E '^[[:space:]]+(iPhone|iPad)' |
    while IFS= read -r line; do
      local state_suffix
      local udid_suffix

      line="${line#"${line%%[![:space:]]*}"}"
      line="${line%"${line##*[![:space:]]}"}"

      runtime_state="${line##* (}"
      runtime_state="${runtime_state%)}"

      state_suffix=" ($runtime_state)"
      line_without_state="${line%$state_suffix}"
      udid="${line_without_state##* (}"
      udid="${udid%)}"
      udid_suffix=" ($udid)"
      name="${line_without_state%$udid_suffix}"

      printf '%s|%s|%s\n' "$name" "$udid" "$runtime_state"
    done
}

test_device() {
  local udid="$1"
  local name="$2"
  local runtime_state="$3"
  local marker="/tmp/start_marker_$udid"
  local device_failures=0

  TOTAL_DEVICES=$((TOTAL_DEVICES + 1))

  echo "==========================================="
  echo "Testing on $name ($udid)"
  echo "Initial state: $runtime_state"
  echo "==========================================="
  
  xcrun simctl boot "$udid" 2>/dev/null || true
  xcrun simctl bootstatus "$udid"
  
  touch "$marker"
  
  echo "Uninstalling $BUNDLE_ID..."
  xcrun simctl uninstall "$udid" "$BUNDLE_ID" 2>/dev/null || true
  
  echo "Installing $APP_PATH..."
  xcrun simctl install "$udid" "$APP_PATH"
  
  local i
  for ((i = 1; i <= LAUNCH_COUNT; i++)); do
    local pid

    TOTAL_LAUNCHES=$((TOTAL_LAUNCHES + 1))

    echo ">>> Launch $i on $name"
    pid="$(xcrun simctl launch "$udid" "$BUNDLE_ID" | grep -oE '[0-9]+$')"
    echo "Launched with PID: $pid"
    echo "Waiting $LAUNCH_WAIT_SECONDS seconds for startup/crash window..."
    sleep "$LAUNCH_WAIT_SECONDS"
    
    # Confirm still running with a simulator-compatible process probe.
    if xcrun simctl spawn "$udid" launchctl procinfo "$pid" >/dev/null 2>&1; then
      echo "SUCCESS: Process $pid is still running after $LAUNCH_WAIT_SECONDS seconds."
    else
      echo "FAILURE: Process $pid is NOT running after $LAUNCH_WAIT_SECONDS seconds."
      device_failures=$((device_failures + 1))
    fi
    
    xcrun simctl terminate "$udid" "$BUNDLE_ID" 2>/dev/null || true
    sleep "$POST_TERMINATE_DELAY_SECONDS"
  done
  
  echo "--- Crash reports for $name ---"
  local crash_reports
  crash_reports="$(find "$CRASH_LOG_ROOT" -name "MusicSalesApp.Maui*" -newer "$marker" 2>/dev/null || true)"
  if [[ -n "$crash_reports" ]]; then
    printf '%s\n' "$crash_reports"
    device_failures=$((device_failures + 1))
  fi
  
  echo "--- Recent logs for $name ---"
  xcrun simctl spawn "$udid" log show --predicate 'process == "MusicSalesApp.Maui"' --style compact --last 2m 2>/dev/null | tail -30

  if [[ "$device_failures" -eq 0 ]]; then
    echo "RESULT: PASS on $name"
  else
    echo "RESULT: FAIL on $name ($device_failures issues)"
  fi

  TOTAL_FAILURES=$((TOTAL_FAILURES + device_failures))
  rm "$marker"
}

if [[ "$SKIP_BUILD" == "1" ]]; then
  echo "Skipping build and reusing existing app bundle."
  echo "Configuration: $BUILD_CONFIGURATION"
  echo "AppSettingsEnvironment: $APP_SETTINGS_ENVIRONMENT"
  echo "RuntimeIdentifier: $RUNTIME_IDENTIFIER"
else
  build_app_bundle
fi

if [[ ! -d "$APP_PATH" ]]; then
  echo "App bundle not found after build: $APP_PATH" >&2
  exit 1
fi

while IFS='|' read -r name udid runtime_state; do
  [[ -n "$name" ]] || continue

  if ! should_test_device "$name"; then
    continue
  fi

  test_device "$udid" "$name" "$runtime_state"
done < <(list_target_devices)

echo "==========================================="
echo "Crash test summary"
echo "Devices tested: $TOTAL_DEVICES"
echo "Launches tested: $TOTAL_LAUNCHES"
echo "Failures detected: $TOTAL_FAILURES"
echo "==========================================="

if [[ "$TOTAL_DEVICES" -eq 0 ]]; then
  echo "No available iPhone or iPad simulators were found." >&2
  exit 1
fi

if [[ "$TOTAL_FAILURES" -ne 0 ]]; then
  exit 1
fi
