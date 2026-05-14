#!/bin/zsh

set -euo pipefail

workspace_root="$(cd "$(dirname "$0")/.." && pwd)"
output_dir="$workspace_root/DeviceLogs/ios-runtime"
temp_dir="$output_dir/.tmp"
devices_json="$temp_dir/devices.json"
files_json="$temp_dir/files.json"
bundle_id="net.streamtunes.musicsalesapp.maui"

mkdir -p "$output_dir" "$temp_dir"

echo "Locating connected iPhone..."
xcrun devicectl list devices --json-output "$devices_json" >/dev/null

device_name="$(python3 - <<'PY' "$devices_json"
import json
import sys

with open(sys.argv[1], 'r', encoding='utf-8') as handle:
    payload = json.load(handle)

for device in payload.get('result', {}).get('devices', []):
    hardware = device.get('hardwareProperties', {})
    name = device.get('deviceProperties', {}).get('name')
    if hardware.get('platform') == 'iOS' and name:
        print(name)
        break
PY
)"

if [[ -z "$device_name" ]]; then
  echo "No connected iPhone was found." >&2
  exit 1
fi

echo "Listing runtime logs for $device_name..."
xcrun devicectl device info files \
  --device "$device_name" \
  --domain-type appDataContainer \
  --domain-identifier "$bundle_id" \
  --subdirectory Library/logs \
  --no-recurse \
  --json-output "$files_json" >/dev/null

latest_log_relative_path="$(python3 - <<'PY' "$files_json"
import json
import sys

with open(sys.argv[1], 'r', encoding='utf-8') as handle:
    payload = json.load(handle)

files = payload.get('result', {}).get('files', [])
logs = []
for item in files:
  name = item.get('name', '')
  relative_path = item.get('relativePath', name)
  if name.startswith('streamtunes-') and name.endswith('.log'):
    logs.append((name, relative_path))

if logs:
  print(f"Library/logs/{sorted(logs, key=lambda entry: entry[0])[-1][1]}")
PY
)"

if [[ -z "$latest_log_relative_path" ]]; then
  echo "No iPhone runtime log files were found under Library/logs." >&2
  exit 1
fi

local_file_path="$output_dir/${latest_log_relative_path##*/}"
latest_alias_path="$output_dir/streamtunes-latest-device.log"

echo "Copying $latest_log_relative_path from $device_name"
xcrun devicectl device copy from \
  --device "$device_name" \
  --domain-type appDataContainer \
  --domain-identifier "$bundle_id" \
  --source "$latest_log_relative_path" \
  --destination "$local_file_path"

cp "$local_file_path" "$latest_alias_path"

echo
echo "Newest on-device runtime log saved to: $local_file_path"
echo "Stable latest-log alias updated at: $latest_alias_path"
echo
echo "Recent Apple-related lines:"
rg -n "Apple App Store|AppStoreBillingService|streamtunes_monthly_sub_ios|Invalid issuer|access was denied|purchase verification failed" "$local_file_path" | tail -n 80 || true