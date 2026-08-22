# Producing App Store screenshots

App Store Connect blocks "Add for Review" until a screenshot exists for every required display
size. This is how the current set was produced, written down because almost every step has a trap
that fails *silently* — you get a valid-looking image that Apple rejects, or that quietly advertises
the wrong thing.

Everything here runs against the iOS Simulator. No device is needed.

## The required sizes, and which simulator gives them

| App Store slot | Pixels | Simulator device |
| --- | --- | --- |
| 6.5-inch iPhone | 1284 × 2778 | **iPhone 13 Pro Max** |
| 13-inch iPad | 2064 × 2752 | **iPad Pro 13-inch (M5)** |

**The 6.5-inch slot is the one people get wrong.** iPhone *14 Pro Max* sounds like the right device
and is not — it has the Dynamic Island resolution, 1290 × 2796, which Apple assigns to a different
slot and rejects here. Apple accepts 1242 × 2688 **or** 1284 × 2778 for 6.5-inch; the 12 Pro Max and
13 Pro Max give the latter. The older 1242 × 2688 devices (Xs Max, 11 Pro Max) can no longer be
created, because iOS 26 runtimes do not support them.

Always verify rather than trusting the device name:

```bash
sips -g pixelWidth -g pixelHeight shot.png
```

## Four things that silently ruin a screenshot

1. **Build Production, not Test.** A Test build renders a banner across the top reading
   *"Streamtunes Testing — Backend Server is https://davidtest.dev"*. It will go straight into your
   store listing. Build with `-p:AppSettingsEnvironment=Production`.
2. **Sign in first.** Anonymous playback shows an orange **"Preview Only (60 seconds)"** badge on
   the player and pops a *"Preview Limit"* dialog after 60 seconds. Signed in, that badge becomes a
   blue ✓ **"Unlimited Access"**, and Home gains a *Your Playlists* section — both much better for a
   listing. **Each simulator needs its own login**; they have separate keychains.
3. **JPEG, not PNG.** App Store Connect rejects images with an alpha channel, and `simctl` always
   writes PNG with alpha. Convert before uploading (below).
4. **Set the status bar.** Apple's own convention is 9:41 with full bars and a charged battery.

## The steps

```bash
# 1. Production build for the simulator
dotnet build MusicSalesApp.Maui/MusicSalesApp.Maui.csproj -f net10.0-ios -c Release \
  -p:RuntimeIdentifier=iossimulator-arm64 -p:AppSettingsEnvironment=Production \
  -p:ValidateXcodeVersion=false

# 2. Create the two devices
RT=com.apple.CoreSimulator.SimRuntime.iOS-26-5
xcrun simctl create Shot65   com.apple.CoreSimulator.SimDeviceType.iPhone-13-Pro-Max      "$RT"
xcrun simctl create ShotPad13 com.apple.CoreSimulator.SimDeviceType.iPad-Pro-13-inch-M5-12GB "$RT"

# 3. Boot, install, launch (per udid)
APP=MusicSalesApp.Maui/bin/Release/net10.0-ios/iossimulator-arm64/MusicSalesApp.Maui.app
xcrun simctl boot "$UDID"; xcrun simctl bootstatus "$UDID" -b
xcrun simctl install "$UDID" "$APP"
xcrun simctl launch "$UDID" net.streamtunes.musicsalesapp.maui

# 4. Apple-standard status bar
xcrun simctl status_bar "$UDID" override \
  --time "9:41" --batteryState charged --batteryLevel 100 --cellularBars 4 --wifiBars 3

# 5. Open the GUI so a human can sign in - simctl/idb alone never shows a window
open -a Simulator

# 6. Capture, then convert (JPEG drops the alpha Apple rejects)
xcrun simctl io "$UDID" screenshot shot.png
sips -s format jpeg -s formatOptions 95 shot.png --out shot.jpg
```

## Driving the UI

`xcrun simctl` **cannot tap** — its `ui` subcommand only sets appearance. Use `idb`:

```bash
brew install facebook/fb/idb-companion          # the gRPC server
/usr/bin/python3 -m venv idbenv                 # MUST be Python 3.9, see below
idbenv/bin/pip install fb-idb
idbenv/bin/idb connect "$UDID"
idbenv/bin/idb ui tap --udid "$UDID" <x> <y>    # POINTS, not pixels
```

**`fb-idb` does not work on Python 3.14** — it calls `asyncio.get_event_loop()`, which now raises
`RuntimeError: There is no current event loop`. Homebrew's `python3` is 3.14; macOS's
`/usr/bin/python3` is 3.9 and works. Build the venv from that one.

Coordinates are **points**, not pixels: divide pixel coordinates by the scale factor (3 for these
iPhones, 2 for the iPad). iPhone 13 Pro Max is 428 × 926 points; iPad Pro 13-inch is 1024 × 1366.

**Flyout item positions move once you sign in.** Signed out the menu is Home / Music Library / Login
/ Register / Config; signed in it becomes Home / Music Library / My Playlists / Account Settings /
Config / Contact Us / Logout. A tap coordinate memorised from the signed-out menu will hit the wrong
row — screenshot the open flyout and re-measure rather than reusing numbers.

## Never capture the login screen

The sign-in form retains the email address, and a screenshot of it leaks the account into whatever
you hand over. Capture around it, and delete any frame that catches it.
