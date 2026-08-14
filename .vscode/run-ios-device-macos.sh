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
bundle_id="net.streamtunes.musicsalesapp.maui"

mlaunch_candidates=(/usr/local/share/dotnet/packs/Microsoft.iOS.Sdk.net10.0_*/*/tools/bin/mlaunch(N))
if [[ ${#mlaunch_candidates[@]} -eq 0 ]]; then
  echo "Unable to locate mlaunch in the installed .NET iOS workload. Reinstall or repair the MAUI iOS workload first." >&2
  exit 1
fi

mlaunch_path="$(printf '%s\n' "${mlaunch_candidates[@]}" | sort -V | tail -n 1)"

# Only the connected "== Devices ==" block counts. The previous sed range ran to
# "== Simulators ==", which swallowed the "== Devices Offline ==" section in
# between, so any offline iPhone/iPad inflated the count and tripped the
# "Multiple physical iOS devices detected" bail-out below.
device_lines="$(xcrun xctrace list devices | awk '/^== Devices ==$/{f=1;next} /^== /{f=0} f' | grep -E 'iPhone|iPad|iPod' || true)"
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

ios_build_args=(
  -p:AppSettingsEnvironment="$app_environment"
  -p:RuntimeIdentifier=ios-arm64
  -p:ValidateXcodeVersion=false
)

dotnet build "$project_path" -f net10.0-ios -c Debug "${ios_build_args[@]}"
"$mlaunch_path" --installdev="$app_bundle" --devname "$device_udid" --install-progress
"$mlaunch_path" --launchdevbundleid "$bundle_id" --devname "$device_udid"