#!/bin/zsh

set -euo pipefail

workspace_root="$(cd "$(dirname "$0")/.." && pwd)"
output_dir="$workspace_root/DeviceLogs"
timestamp="$(date +%Y%m%d-%H%M%S)"
archive_path="$output_dir/iphone-$timestamp.logarchive"
filtered_path="$output_dir/iphone-$timestamp-filtered.log"
predicate='eventMessage CONTAINS[c] "AppStoreBillingService" OR eventMessage CONTAINS[c] "Apple App Store" OR eventMessage CONTAINS[c] "streamtunes_monthly_sub_ios" OR processImagePath CONTAINS[c] "MusicSalesApp"'

mkdir -p "$output_dir"

echo "Collecting the last 30 minutes of logs from the first connected iPhone."
echo "macOS may prompt for your password because device log collection requires sudo."
echo "Archive: $archive_path"

sudo log collect \
  --device \
  --last 30m \
  --output "$archive_path" \
  --predicate "$predicate"

echo "Extracting filtered log lines to $filtered_path"
log show \
  --archive "$archive_path" \
  --style compact \
  --predicate "$predicate" > "$filtered_path"

echo
echo "Saved files:"
echo "  $archive_path"
echo "  $filtered_path"
echo
echo "Recent matching lines:"
tail -n 200 "$filtered_path"