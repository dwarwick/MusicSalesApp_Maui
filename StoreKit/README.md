# StoreKit Simulator Assets

Use this folder for Xcode `.storekit` files that support simulator-only StoreKit experiments.

Recommended file name:

- `StreamTunes.storekit`

Recommended creation flow:

1. Run the VS Code task `maui-open-storekit-folder-in-xcode`.
2. In Xcode, create a new `StoreKit Configuration File`.
3. Select `Sync this file with an app in App Store Connect` if you want Xcode to mirror the live subscription metadata.
4. Choose the `Streamtunes` app.
5. Save the file into this folder.

Important limitations for this repo:

- The normal MAUI simulator run path uses `dotnet build -t:Run`, not an Xcode scheme.
- That means a `.storekit` file stored here does not automatically become active for the MAUI app when you launch it from the existing VS Code simulator tasks.
- This folder is still useful for keeping the expected StoreKit configuration under source control and ready for Xcode-based experiments.
- Real App Store sandbox purchase verification still requires a physical iPhone or a TestFlight build on a device.