# Apple Development Setup

This repo already targets iOS and Mac Catalyst in `MusicSalesApp.Maui/MusicSalesApp.Maui.csproj`.
The missing pieces for Apple development are local workspace setup on macOS, Xcode simulator runtimes, and Apple-specific VS Code tasks.

## Recommended Workspace Setup

Use local filesystem clones for active development instead of the GitHub-backed `vscode-vfs` workspace.

Why this is recommended:

- Reduces repeated external-directory approval prompts in VS Code and Copilot.
- Keeps `.vscode` tasks, scripts, and search operations inside the workspace boundary.
- Makes `dotnet`, Xcode, and simulator tooling behave predictably from Terminal and VS Code.

Recommended local layout:

```text
~/src/MusicSalesApp_Maui
~/src/MusicSalesApp
```

Then open `MusicSalesApp.local.code-workspace` from the `MusicSalesApp_Maui` repo root.

This workspace file assumes the two repos are cloned side by side, with `MusicSalesApp` next to `MusicSalesApp_Maui`.

## Recommended VS Code Extensions

The repo now includes `.vscode/extensions.json` with these recommendations:

- `ms-dotnettools.csdevkit`
- `ms-dotnettools.csharp`

Install those in the local workspace before using the launch configurations.

## Prerequisites

Install and verify these before trying to run the Apple targets:

1. Xcode on macOS.
2. Xcode Command Line Tools selected for the installed Xcode version.
3. .NET SDK version required by this repo.
4. MAUI workloads, including Apple targets.
5. At least one iOS simulator runtime installed in Xcode.

Useful checks:

```bash
dotnet --version
dotnet workload list
xcode-select -p
xcrun simctl list devices available
```

If Xcode was just installed or updated, run:

```bash
sudo xcodebuild -runFirstLaunch
```

## Install iPhone Simulator Runtimes

Open Xcode, then go to `Xcode > Settings > Components`.

Install:

- The current iOS simulator runtime you want for daily development.
- Optionally, one older iOS simulator runtime if you need compatibility checks.

Mac Catalyst does not use a separate macOS emulator. It runs as a native Mac app on your machine.

## Install or Repair MAUI Workloads

If the Apple workloads are missing on this Mac, install or repair them:

```bash
sudo dotnet workload install maui
```

If you already have MAUI installed but Apple targets are not working, try:

```bash
sudo dotnet workload repair
```

## VS Code Tasks Added For Apple Development

The repo now includes these tasks in `.vscode/tasks.json`:

- `maui-check-apple-prereqs`
- `maui-open-xcode-app-store`
- `maui-list-ios-simulators`
- `maui-list-ios-devices`
- `maui-boot-ios-simulator`
- `maui-run-ios-simulator`
- `maui-run-ios-device`
- `maui-run-ios-device-test`
- `maui-run-ios-device-production`
- `maui-build-maccatalyst`
- `maui-run-maccatalyst`

The repo also includes Mac Catalyst launch configurations in `.vscode/launch.json`:

- `MAUI - Mac Catalyst (ARM64)`
- `MAUI - Mac Catalyst (x64)`

### iOS Simulator Workflow

If you are not sure whether this Mac is ready yet, run `maui-check-apple-prereqs` first.

1. Run `maui-list-ios-simulators`.
2. Copy the UDID for the simulator you want.
3. Run `maui-run-ios-simulator` and paste the UDID when prompted.

The run task boots the simulator and then launches the MAUI app with:

```bash
dotnet build -t:Run -f net10.0-ios -c Debug -p:_DeviceName=:v2:udid=<SIMULATOR_UDID>
```

### Mac Catalyst Workflow

Run `maui-run-maccatalyst` and choose the runtime identifier that matches your Mac:

- `maccatalyst-arm64` for Apple silicon Macs.
- `maccatalyst-x64` for Intel Macs.

Use `maui-build-maccatalyst` if you only want to compile without launching.

If you want to debug the Mac build from VS Code, use the launch configuration that matches your Mac architecture.

## Direct CLI Commands

These are the equivalent commands if you prefer Terminal.

Run on iOS simulator:

```bash
dotnet build MusicSalesApp.Maui/MusicSalesApp.Maui.csproj \
  -t:Run \
  -f net10.0-ios \
  -c Debug \
  -p:_DeviceName=:v2:udid=<SIMULATOR_UDID>
```

Run on a connected iPhone or iPad:

```bash
dotnet build MusicSalesApp.Maui/MusicSalesApp.Maui.csproj \
  -f net10.0-ios \
  -c Debug \
  -p:RuntimeIdentifier=ios-arm64

dotnet build MusicSalesApp.Maui/MusicSalesApp.Maui.csproj \
  -t:Run \
  -f net10.0-ios \
  -c Debug \
  -p:RuntimeIdentifier=ios-arm64 \
  -p:_DeviceName=<DEVICE_UDID>
```

Run on Mac Catalyst:

```bash
dotnet build MusicSalesApp.Maui/MusicSalesApp.Maui.csproj \
  -t:Run \
  -f net10.0-maccatalyst \
  -c Debug \
  -p:RuntimeIdentifier=maccatalyst-arm64
```

## Real iPhone Workflow

The workspace now includes physical-device helper tasks, but Apple signing still has to be prepared on this Mac.
Do not commit personal signing details into the shared project unless you later confirm they are required for everyone.

### Recommended First-Time Setup

1. Install Xcode and complete first-launch setup if you have not already.
2. Open Xcode and sign in at `Xcode > Settings > Accounts` with the Apple ID you want to use for device testing.
3. Connect the iPhone by cable.
4. Unlock the phone and accept both the `Trust This Computer` prompt on the device and any trust prompt on the Mac.
5. If the phone asks for Developer Mode after the first install attempt, enable it and reboot the phone when prompted.

### Using a Free Personal Team

If you do not have a paid Apple Developer membership, you can still test on a personal device with Xcode's Personal Team signing.
That is the recommended first step for local testing.

Expected limitations:

- Signing is local to this Mac and Apple ID.
- Provisioning is temporary and may require reinstalling later.
- TestFlight and App Store distribution are not available.

Before the first MAUI device build will work, this Mac also needs an `Apple Development` signing identity in Keychain.
If `security find-identity -v -p codesigning` reports `0 valid identities found`, create the certificate in Xcode first.

Use this Xcode path:

1. `Xcode > Settings > Accounts`
2. Select your Apple ID.
3. Click `Manage Certificates...`
4. Click `+`
5. Add `Apple Development`

After that, retry the device build.

### Finding the Connected Device UDID

Run `maui-list-ios-devices` after the phone is connected, unlocked, and trusted.

If no phone appears, open Xcode once and keep the phone unlocked while it finishes device support setup.

### Running on the Connected Device

1. Run `maui-list-ios-devices`.
2. Leave only the phone you want to target connected if you have more than one iPhone or iPad attached.
3. Run `maui-run-ios-device` for the Development environment, `maui-run-ios-device-test` for the Test environment, or `maui-run-ios-device-production` for the Production environment.

These device tasks build the app for `ios-arm64`, install the generated `.app` bundle onto the connected device with `mlaunch`, and then launch the installed bundle identifier on the phone.

### If Signing Blocks the First Attempt

Typical first-run issues are local Apple setup problems, not repo configuration problems.

Check these first:

- Xcode is signed in with the Apple ID you want to use.
- The phone is unlocked and trusted.
- Xcode can see the phone in `Window > Devices and Simulators`.
- The bundle identifier `net.streamtunes.musicsalesapp.maui` does not conflict with another locally signed app on your Apple ID.
- An `Apple Development` certificate exists in your login keychain.

If the build still fails on signing, resolve the team or provisioning issue locally first before adding any project-level signing properties.

## Troubleshooting

### Repeated "Allow reading external directory" prompts

If you still see these prompts while working from a GitHub-backed or virtual workspace, move to local clones and open those local folders directly in VS Code.

### Xcode is not installed yet

Run `maui-open-xcode-app-store` from VS Code to reopen the Xcode App Store page, install Xcode, then run `sudo xcodebuild -runFirstLaunch` in Terminal after installation finishes.

### `xcrun simctl` not found

Xcode is either not installed correctly, not selected via `xcode-select`, or first-launch setup has not completed.

### iOS task fails before build starts

Usually this means one of these is missing:

- the simulator UDID is invalid
- the simulator runtime is not installed
- the MAUI Apple workloads are not installed
- the selected Xcode version is incompatible with the installed workloads

### Real device does not appear in `maui-list-ios-devices`

Usually this means one of these is still incomplete:

- the phone is not connected by cable
- the phone is locked
- the `Trust This Computer` step has not been accepted
- Xcode has not finished preparing device support for that phone
- the phone is connected through a charge-only cable or adapter

Open Xcode once, keep the phone unlocked, and retry the task.

### Real device build fails with signing or provisioning errors

Start with the local Apple state, not the repo.

Check these first:

- the Apple ID is signed into Xcode
- a Personal Team or paid team is available in Xcode
- the device is visible in Xcode's device window
- Developer Mode is enabled on the phone if prompted

Only add committed signing configuration after you have one successful local install and know exactly which values are required.

### Mac Catalyst fails on Apple silicon

Choose `maccatalyst-arm64` in the task prompt unless you intentionally want to run an x64 build under Rosetta.