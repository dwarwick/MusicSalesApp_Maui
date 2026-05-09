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
- `maui-boot-ios-simulator`
- `maui-run-ios-simulator`
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

Run on Mac Catalyst:

```bash
dotnet build MusicSalesApp.Maui/MusicSalesApp.Maui.csproj \
  -t:Run \
  -f net10.0-maccatalyst \
  -c Debug \
  -p:RuntimeIdentifier=maccatalyst-arm64
```

## Real iPhone Deployment Later

Physical iPhone deployment is not part of the simulator tasks above.
When you are ready for device testing, you will also need:

- An Apple Developer account.
- A signing team selected in Xcode.
- Provisioning for the bundle identifier.
- A connected device UDID.

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

### Mac Catalyst fails on Apple silicon

Choose `maccatalyst-arm64` in the task prompt unless you intentionally want to run an x64 build under Rosetta.