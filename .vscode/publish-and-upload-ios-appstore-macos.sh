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
skip_smoke_test=false
skip_device_test=false
smoke_profile=""

usage() {
  print -u2 "Usage: ${0:t} [Production|Test] [--dry-run] [--skip-smoke-test] [--skip-device-test] [--smoke-profile quick|full]"
}

for arg in "$@"; do
  case "$arg" in
    Production|Test)
      environment="$arg"
      ;;
    --dry-run)
      dry_run=true
      ;;
    --skip-smoke-test)
      skip_smoke_test=true
      ;;
    --skip-device-test)
      skip_device_test=true
      ;;
    --smoke-profile=*)
      smoke_profile="${arg#*=}"
      ;;
    *)
      usage
      exit 1
      ;;
  esac
done

# A Production upload goes to App Store review, which launches the app on real
# hardware we cannot all test on, so it gets the full simulator matrix.
if [[ -z "$smoke_profile" ]]; then
  if [[ "$environment" == "Production" ]]; then
    smoke_profile="full"
  else
    smoke_profile="quick"
  fi
fi

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

# Launch-crash gate. Deliberately runs BEFORE the ApplicationVersion bump so a
# failure does not burn a build number, and before the keychain read so it can
# fail without touching credentials.
if [[ "$skip_smoke_test" == true ]]; then
  print "Skipping the iOS simulator launch-crash gate (--skip-smoke-test)."
else
  print "Running the iOS simulator launch-crash gate (profile: $smoke_profile)..."
  if ! "$workspace_dir/test_sims.sh" --environment "$environment" --profile "$smoke_profile"; then
    print -u2 ""
    print -u2 "Simulator launch-crash gate FAILED - not publishing."
    print -u2 "See DeviceLogs/simulator-smoke/latest/summary.txt, then the failing"
    print -u2 "device's launch-N.stdio.log for the managed stack trace."
    print -u2 "Re-run with --skip-smoke-test to override."
    exit 1
  fi
  print "Launch-crash gate passed."
fi

# The simulator gate JITs and so cannot exercise LLVM AOT. This one builds
# ios-arm64 Release - the same codegen App Store review runs - and launch-tests
# it on the paired iPhone. Production goes to review, so it is gated; Test does
# not, so it is only advisory.
if [[ "$skip_device_test" == true ]]; then
  print "Skipping the physical-device launch-crash gate (--skip-device-test)."
elif [[ "$environment" != "Production" ]]; then
  print "Skipping the physical-device gate (only enforced for Production)."
else
  print "Running the physical-device launch-crash gate on the paired iPhone..."
  if ! "$workspace_dir/test_device.sh" --environment "$environment"; then
    print -u2 ""
    print -u2 "Physical-device launch-crash gate FAILED - not publishing."
    print -u2 "See DeviceLogs/device-smoke/latest/summary.txt."
    print -u2 "If no iPhone is paired, connect and unlock it, or re-run with"
    print -u2 "--skip-device-test to override."
    exit 1
  fi
  print "Physical-device gate passed."

  # That gate signed ios-arm64 Release with the Apple Development identity. Wipe
  # it so the distribution publish below cannot reuse a development-signed
  # artifact through incremental build.
  print "Clearing the development-signed device build before the distribution publish..."
  rm -rf "$project_dir/bin/Release/net10.0-ios/ios-arm64"
fi

print "Reminder: this validates launch, not full functionality. The simulator gate"
print "covers iPad, which we own no hardware for."

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