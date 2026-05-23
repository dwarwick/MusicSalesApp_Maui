#!/usr/bin/env zsh

set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "Usage: $0 <Development|Test|Production>" >&2
  exit 1
fi

app_environment="$1"
workspace_root="$(cd "$(dirname "$0")/.." && pwd)"
project_path="$workspace_root/MusicSalesApp.Maui/MusicSalesApp.Maui.csproj"
app_bundle="$workspace_root/MusicSalesApp.Maui/bin/Debug/net10.0-ios/ios-arm64/MusicSalesApp.Maui.app"
mlaunch_path="/usr/local/share/dotnet/packs/Microsoft.iOS.Sdk.net10.0_26.4/26.4.10259/tools/bin/mlaunch"
bundle_id="net.streamtunes.musicsalesapp.maui"

device_lines="$(xcrun xctrace list devices | sed -n '/== Devices ==/,/== Simulators ==/p' | grep -E 'iPhone|iPad|iPod' || true)"
device_count="$(printf '%s\n' "$device_lines" | sed '/^$/d' | wc -l | tr -d ' ')"

if [[ "$device_count" -eq 0 ]]; then
  echo "No physical iOS device detected. Connect and unlock the device, trust this Mac, and open Xcode once." >&2
  exit 1
fi

if [[ "$device_count" -gt 1 ]]; then
  printf '%s\n' "$device_lines" >&2
  echo "Multiple physical iOS devices detected. Disconnect extras or use maui-list-ios-devices to choose one." >&2
  exit 1
fi

device_udid="$(printf '%s\n' "$device_lines" | awk -F '[()]' 'NF >= 2 { print $(NF-1) }')"

dotnet build "$project_path" -f net10.0-ios -c Debug -p:AppSettingsEnvironment="$app_environment" -p:RuntimeIdentifier=ios-arm64
"$mlaunch_path" --installdev="$app_bundle" --devname "$device_udid" --install-progress
"$mlaunch_path" --launchdevbundleid "$bundle_id" --devname "$device_udid"