#!/bin/zsh

# Builds and installs onto a PHYSICAL Android device. The macOS counterpart of deploy-phone.ps1.
#
# Kept separate from deploy-android-macos.sh, which targets an emulator, because the two differ in
# more than the serial:
#
#   * Fast Deployment does not work here. It pushes loose assemblies into
#     files/.__override__/<abi>/ inside the app's data directory, and on a physical device that
#     fails with "error XA0127: could not set read permissions ... No such file or directory".
#     EmbedAssembliesIntoApk=true bypasses it and installs a whole APK instead, which is what
#     deploy-phone.ps1 does on Windows for the same reason. The cost is that every install
#     re-pushes the full APK.
#
#   * The emulator script uninstalls first to clear that same state. Not done here: an uninstall
#     wipes the signed-in session and the downloaded audio cache, which is usually the very thing
#     you are on the device to test.
#
# Usage: deploy-phone-macos.sh [--serial <serial>] [--configuration Debug|Release]
#                              [--appsettings-environment <env>] [--no-launch]
#
# Configuration picks the backend: Debug -> davidtest.dev (test), Release -> streamtunes.net.

set -euo pipefail

source "$HOME/.zshrc" >/dev/null 2>&1 || true

script_dir="${0:A:h}"
csproj="${script_dir}/../MusicSalesApp.Maui/MusicSalesApp.Maui.csproj"

: "${ANDROID_SDK_ROOT:=$HOME/Library/Android/sdk}"
adb="$ANDROID_SDK_ROOT/platform-tools/adb"
package_name="net.streamtunes.musicsalesapp.maui"

serial=""
configuration="Debug"
appsettings_environment=""
launch=1

while [[ $# -gt 0 ]]; do
  case "$1" in
    --serial) serial="${2:-}"; shift 2 ;;
    --configuration) configuration="${2:-}"; shift 2 ;;
    --appsettings-environment) appsettings_environment="${2:-}"; shift 2 ;;
    --no-launch) launch=0; shift ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

if [[ ! -x "$adb" ]]; then
  echo "adb was not found under $ANDROID_SDK_ROOT/platform-tools." >&2
  exit 1
fi

# Physical devices only - an emulator serial is always "emulator-NNNN". Restricted to the "device"
# state so a phone sitting at an unaccepted USB-debugging prompt ("unauthorized") or still waking up
# ("offline") is reported as such rather than being built for and failing at the install.
if [[ -z "$serial" ]]; then
  devices=("${(@f)$($adb devices | awk '$2 == "device" && $1 !~ /^emulator-/ { print $1 }')}")
  devices=("${(@)devices:#}")

  if (( ${#devices[@]} == 0 )); then
    echo "No physical Android device is attached and authorized." >&2
    echo "Check '$adb devices': an 'unauthorized' entry needs the USB-debugging prompt accepted on the phone." >&2
    echo "To target an emulator instead, use the maui-deploy-android task." >&2
    exit 1
  fi

  if (( ${#devices[@]} > 1 )); then
    echo "More than one physical device is attached: ${devices[*]}" >&2
    echo "Re-run with --serial <serial> to choose one." >&2
    exit 1
  fi

  serial="${devices[1]}"
fi

model="$($adb -s "$serial" shell getprop ro.product.model 2>/dev/null | tr -d '\r')"
echo "=== Building and installing ($configuration) onto ${model:-device} [$serial] ==="

build_args=(
  build "$csproj"
  -f net10.0-android
  -c "$configuration"
  -t:Install
  # See the header: Fast Deployment cannot write into the app's data directory on a real device.
  -p:EmbedAssembliesIntoApk=true
  "-p:AdbTarget=-s%20$serial"
)

if [[ -n "$appsettings_environment" ]]; then
  build_args+=("-p:AppSettingsEnvironment=$appsettings_environment")
  echo "  Using appsettings environment: $appsettings_environment"
fi

dotnet "${build_args[@]}"

if (( launch )); then
  echo "=== Launching app ==="
  $adb -s "$serial" shell monkey -p "$package_name" -c android.intent.category.LAUNCHER 1 >/dev/null
fi

echo "Done!"
