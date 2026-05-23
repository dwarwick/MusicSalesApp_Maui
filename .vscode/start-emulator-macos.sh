#!/bin/zsh

set -euo pipefail

source "$HOME/.zshrc" >/dev/null 2>&1 || true

: "${ANDROID_SDK_ROOT:=$HOME/Library/Android/sdk}"

adb="$ANDROID_SDK_ROOT/platform-tools/adb"
emulator="$ANDROID_SDK_ROOT/emulator/emulator"
avd_name="${1:-}"

if [[ ! -x "$adb" || ! -x "$emulator" ]]; then
  echo "Android SDK tools were not found under $ANDROID_SDK_ROOT." >&2
  exit 1
fi

running_serial="$($adb devices | awk '/^emulator-[0-9]+\tdevice$/ { print $1; exit }')"
if [[ -n "$running_serial" ]]; then
  echo "Emulator already running: $running_serial"
  exit 0
fi

if [[ -z "$avd_name" ]]; then
  echo "No Android AVD name was provided." >&2
  echo "Available AVDs:" >&2
  "$emulator" -list-avds >&2 || true
  echo "Create an emulator in Android Studio Device Manager, then rerun this task." >&2
  exit 1
fi

echo "Starting emulator '$avd_name'..."
nohup "$emulator" -avd "$avd_name" -gpu auto -no-snapshot-load >/tmp/maui-android-emulator.log 2>&1 &

timeout_secs=180
elapsed=0

while (( elapsed < timeout_secs )); do
  serial="$($adb devices | awk '/^emulator-[0-9]+\t(device|offline)$/ { print $1; exit }')"
  if [[ -n "$serial" ]]; then
    boot_completed="$($adb -s "$serial" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')"
    if [[ "$boot_completed" == "1" ]]; then
      echo "Emulator booted: $serial"
      exit 0
    fi
  fi

  sleep 5
  (( elapsed += 5 ))
done

echo "WARNING: Emulator boot timed out. Check /tmp/maui-android-emulator.log for details." >&2
exit 1