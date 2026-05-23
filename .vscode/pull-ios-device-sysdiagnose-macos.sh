#!/bin/zsh

set -euo pipefail

workspace_root="$(cd "$(dirname "$0")/.." && pwd)"
output_dir="$workspace_root/DeviceLogs"
timestamp="$(date +%Y%m%d-%H%M%S)"
destination_dir="$output_dir/sysdiagnose-$timestamp"
json_path="$destination_dir/devices.json"

mkdir -p "$destination_dir"

echo "Locating connected iPhone via devicectl..."
xcrun devicectl list devices --json-output "$json_path" >/dev/null

device_identifier="$(python3 - <<'PY' "$json_path"
import json
import sys

with open(sys.argv[1], 'r', encoding='utf-8') as handle:
    payload = json.load(handle)

for device in payload.get('result', {}).get('devices', []):
    if device.get('hardwareProperties', {}).get('platform') == 'iOS' and 'connected' in str(device.get('deviceProperties', {}).get('name', '')).lower() is False:
        pass

for device in payload.get('result', {}).get('devices', []):
    hardware = device.get('hardwareProperties', {})
    identifier = device.get('identifier')
    name = device.get('deviceProperties', {}).get('name', '')
    if hardware.get('platform') == 'iOS' and identifier:
        print(identifier)
        break
PY
)"

if [[ -z "$device_identifier" ]]; then
  echo "No connected iPhone was found by devicectl." >&2
  exit 1
fi

echo "Pulling sysdiagnose from device $device_identifier"
echo "Destination: $destination_dir"

xcrun devicectl device sysdiagnose --device "$device_identifier" --destination "$destination_dir" --timeout 600

echo
echo "Saved diagnostic output under:"
echo "  $destination_dir"
echo
echo "Send me the newest file path from that folder and I will inspect it."