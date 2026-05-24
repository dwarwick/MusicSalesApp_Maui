#!/bin/zsh

set -euo pipefail

script_dir=${0:A:h}
workspace_dir=${script_dir:h}
project_dir="$workspace_dir/MusicSalesApp.Maui"
csproj="$project_dir/MusicSalesApp.Maui.csproj"
ipa_path="$project_dir/bin/Release/net10.0-ios/ios-arm64/publish/MusicSalesApp.Maui.ipa"

apple_id="david.warwick1969@gmail.com"
keychain_item="AC_TESTFLIGHT"
itc_provider="K7ZGP97YV6"
codesign_key="Apple Distribution: David Warwick (K7ZGP97YV6)"
codesign_provision="StreamTunes App Store"
codesign_team_id="K7ZGP97YV6"

environment="Production"
dry_run=false

usage() {
  print -u2 "Usage: ${0:t} [Production|Test] [--dry-run]"
}

for arg in "$@"; do
  case "$arg" in
    Production|Test)
      environment="$arg"
      ;;
    --dry-run)
      dry_run=true
      ;;
    *)
      usage
      exit 1
      ;;
  esac
done

if [[ ! -f "$csproj" ]]; then
  print -u2 "Project file not found: $csproj"
  exit 1
fi

current_build=$(perl -ne 'print "$1\n" if /<ApplicationVersion>(\d+)<\/ApplicationVersion>/' "$csproj" | tail -n 1)
display_version=$(perl -ne 'print "$1\n" if /<ApplicationDisplayVersion>([^<]+)<\/ApplicationDisplayVersion>/' "$csproj" | tail -n 1)

if [[ -z "$current_build" || -z "$display_version" ]]; then
  print -u2 "Could not read ApplicationDisplayVersion/ApplicationVersion from $csproj"
  exit 1
fi

next_build=$(( current_build + 1 ))

print "Environment: $environment"
print "Preparing version: $display_version ($next_build)"

if [[ "$dry_run" == true ]]; then
  exit 0
fi

backup_file=$(mktemp)
cp "$csproj" "$backup_file"
restore_project=true

cleanup() {
  local exit_code=$?

  if [[ $exit_code -ne 0 && "$restore_project" == true && -f "$backup_file" ]]; then
    cp "$backup_file" "$csproj"
    print -u2 "Restored $csproj to build $current_build after failure."
  fi

  rm -f "$backup_file"
  return $exit_code
}

trap cleanup EXIT

perl -0pi -e 's{<ApplicationVersion>(\d+)</ApplicationVersion>}{"<ApplicationVersion>".($1+1)."</ApplicationVersion>"}e' "$csproj"
grep -E '<ApplicationDisplayVersion>|<ApplicationVersion>' "$csproj"

app_password=$(security find-generic-password -s "$keychain_item" -a "$apple_id" -w 2>/dev/null) || {
  print -u2 "Could not read keychain item '$keychain_item' for $apple_id."
  print -u2 "Store the app-specific password in macOS Keychain before running this task."
  exit 1
}

dotnet publish "$csproj" \
  -f net10.0-ios \
  -c Release \
  -p:RuntimeIdentifier=ios-arm64 \
  "-p:AppSettingsEnvironment=$environment" \
  -p:ArchiveOnBuild=true \
  -p:BuildIpa=true \
  "-p:CodesignKey=$codesign_key" \
  "-p:CodesignProvision=$codesign_provision" \
  "-p:CodesignTeamId=$codesign_team_id" \
  -p:ValidateXcodeVersion=false

if [[ ! -f "$ipa_path" ]]; then
  print -u2 "IPA not found after publish: $ipa_path"
  exit 1
fi

log_dir="$HOME/Library/Logs/ContentDelivery"
mkdir -p "$log_dir"
upload_log="$log_dir/ios-build-upload-${environment:l}-$(date +%Y%m%d-%H%M%S).log"

print "Uploading IPA with iTMSTransporter..."
print "Transporter log: $upload_log"

xcrun iTMSTransporter -m upload \
  -assetFile "$ipa_path" \
  -u "$apple_id" \
  -p "$app_password" \
  -itc_provider "$itc_provider" \
  -distribution AppStore \
  -v informational 2>&1 | tee "$upload_log"

restore_project=false

print "Uploaded $display_version ($next_build) for $environment."
print "Transporter log: $upload_log"