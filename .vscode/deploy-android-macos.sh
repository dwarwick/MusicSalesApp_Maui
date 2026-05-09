#!/bin/zsh

set -euo pipefail

source "$HOME/.zshrc" >/dev/null 2>&1 || true

script_dir="${0:A:h}"
project_dir="${script_dir}/../MusicSalesApp.Maui"
csproj="$project_dir/MusicSalesApp.Maui.csproj"

: "${ANDROID_SDK_ROOT:=$HOME/Library/Android/sdk}"

adb="$ANDROID_SDK_ROOT/platform-tools/adb"
package_name="net.streamtunes.musicsalesapp.maui"

if [[ ! -x "$adb" ]]; then
  echo "adb was not found under $ANDROID_SDK_ROOT/platform-tools." >&2
  exit 1
fi

serial="$($adb devices | awk '/^emulator-[0-9]+\tdevice$/ { print $1; exit }')"
if [[ -z "$serial" ]]; then
  echo "No running Android emulator was found. Run maui-start-android-emulator first." >&2
  exit 1
fi

echo "=== Building and Installing ==="
$adb -s "$serial" uninstall "$package_name" >/dev/null 2>&1 || true
dotnet build "$csproj" -f net10.0-android -c Debug -t:Install "-p:AdbTarget=-s%20$serial"

echo "=== Launching app ==="
$adb -s "$serial" shell monkey -p "$package_name" -c android.intent.category.LAUNCHER 1 >/dev/null

echo "Done!"