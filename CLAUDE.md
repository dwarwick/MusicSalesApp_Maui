# CLAUDE.md

## Working Branches

- **Before editing any file**, check the current branch with `git rev-parse --abbrev-ref HEAD`.
- If it is `master`, create and switch to a working branch **first** — do not start editing and branch later.
- Use task-based names such as `work/reduce-user-perceived-anr`.
- Never make code edits on `master` unless the user explicitly asks for that.

## What this is

.NET MAUI mobile app building the **StreamTunes** Android and iOS apps — **listeners only** (no creator/upload/admin features; those live in the sibling Blazor web app). Talks to one of two backends depending on build configuration:

- **Production** (Release builds): https://streamtunes.net
- **Test** (Debug builds): https://davidtest.dev

See `AppConfig.cs:6-15` for the canonical resolution logic, quoted here since it's easy to get wrong:

> Resolution logic:
>   1. If "UseLocalHost" is missing or true → use top-level "ApiBaseUrl" (localhost dev)
>   2. If "UseLocalHost" is false → use "DavidTest:ApiBaseUrl" (remote test server)
>   3. In Production ("UseLocalHost" absent, top-level "ApiBaseUrl" set to production URL) → use top-level "ApiBaseUrl" directly

Concretely: `AppSettingsEnvironment` (an MSBuild property, `Development` for Debug / `Production` for Release) picks which embedded `appsettings.{Environment}.json` layers on top of the base file; `appsettings.Development.json`/`appsettings.Test.json` point at `davidtest.dev`, `appsettings.Production.json` points at `streamtunes.net`. There is no runtime UI toggle — switching backend means switching build configuration. `MauiProgram.cs` additionally rewrites `localhost` to `10.0.2.2` (Android emulator) or `127.0.0.1` (physical device via `adb reverse`) for local dev. `ITestingServerBannerService` shows a banner in the app when running against the non-production server.

## Solution structure

`MusicSalesApp_Maui.slnx` contains exactly two projects:

- **`MusicSalesApp.Maui/`** — the app (`net10.0-android`/`-ios`/`-maccatalyst`/`-windows`), `ApplicationId=net.streamtunes.musicsalesapp.maui`.
- **`MusicSalesApp.Maui.Tests/`** — NUnit + Moq. Not linked via `ProjectReference`; it directly compiles source files from `../MusicSalesApp.Maui/ViewModels/*.cs` and `Services/*.cs` (excluding platform-coupled files like `AlertService.cs`, `SignalRService.cs`, `NavigationService.cs`, `BrowserService.cs`).

There's **no shared library inside this repo**. Both projects reference the sibling Blazor repo's shared project directly:

```
../../MusicSalesApp/MusicSalesApp.Common/MusicSalesApp.Common.csproj
```

(`SubscriptionStatuses`, `BillingProviders`, etc.) — see "Sibling repo" below. Note `AuthStorageKeys` is *not* one of them despite the name: it is local, at `MusicSalesApp.Maui/Services/AuthStorageKeys.cs`, and holds a single key. Every other auth/subscription storage key is a private `const` inside `AuthService`.

## Tech stack

- **.NET 10 MAUI** (`Microsoft.Maui.Controls` 10.0.80), `MauiXamlInflator=SourceGen`.
- **MVVM**: CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]` source generators) throughout.
- **Navigation**: Shell-based, routes centralized in `Services/NavigationRoutes.cs`, wrapped by `INavigationService`.
- **DI**: standard `MauiProgram.CreateMauiApp()` builder — read this file to see the entire service/ViewModel/platform-swap graph in one place.
- **Auth tokens**: server-issued JWT, decoded client-side only (`System.IdentityModel.Tokens.Jwt`) for expiry checks — there is **no refresh-token flow**; once the JWT expires the user must log in again.
- **Playback**: Android uses Media3/ExoPlayer (`Xamarin.AndroidX.Media3.*`); iOS/MacCatalyst/Windows still use the legacy `Plugin.MediaManager` and are **not yet at cache/reliability parity** with Android (see "Playback & cache architecture" below).
- **Other notable packages**: `Microsoft.AspNetCore.SignalR.Client` (live stream/like count updates), `SkiaSharp.Extended.UI.Maui` (equalizer/visualizer drawing), `Xamarin.AndroidX.Biometric`, `Xamarin.Android.Google.BillingClient` (Google Play Billing).
- iOS Release builds carry a documented workaround (`MusicSalesApp.Maui.csproj`, the `net10.0-ios` Release PropertyGroup) for an App Store launch crash on iPadOS 26 (`MtouchRegistrar=static`, `MtouchUseLlvm=true`, etc.) — read the inline comment before touching iOS build settings.
- **Android Release builds use full AOT**, not the SDK default. `.NET`'s Android SDK defaults Release/MonoVM to `RunAOTCompilation=true` **and** `AndroidEnableProfiledAot=true`, which precompiles only the methods in the stock `dotnet.aotprofile` and leaves the rest of the app to JIT on first use — on whatever thread touches it first, which for a UI app is the main thread. A production ANR in 1.0.93 caught the main thread inside `mono_method_get_generic_container` holding Mono's image lock. The csproj now pins `AndroidEnableProfiledAot=false`; cost is roughly +12 MB of install size and ~3 minutes of publish time. Note `AndroidAotMode` is still `Normal`, so a JIT fallback remains — this reduces first-use JIT, it does not eliminate it.

## Core services

- **`AuthService`/`IAuthService`** — login/register, Google sign-in (OAuth code exchange via `api/mobile-auth/google/*`), **Sign in with Apple** (iOS only — see below), biometric login (credentials cached encrypted in `ISecureStorage`, gated by a real biometric prompt via `IBiometricAuthenticator` — AndroidX `BiometricPrompt` on Android, `LAContext`/Face ID/Touch ID on iOS, and a `NoOp`-style fallback elsewhere), JWT session restore/expiry, and subscription/creator status caching (refreshed via `api/subscription/status`). Fires `AuthStateChanged` (marshaled to the main thread) that `AppShell`, `HomeViewModel`, and `AccountSettingsViewModel` all subscribe to.
- **Sign in with Apple** (`IAppleSignInService`, iOS only) — required by App Store guideline 4.8 once Google sign-in is offered, and the reason 1.0.108 was rejected. Deliberately **not** shaped like the Google flow: Google is server-brokered (`WebAuthenticator` → `api/mobile-auth/google/start` → `streamtunes://auth` deep link), whereas Apple uses the **native `ASAuthorizationController` sheet**, so the app itself receives the identity token and posts it to `api/mobile-auth/apple/token`. Consequently there is no `start`/`callback`/`exchange` round trip, no `AddApple` handler on the server, and no `.p8` client secret — the server only verifies the JWT against Apple's JWKS.

  Two things about it are easy to get wrong:

  > **Apple sends the email and name on the FIRST authorization only.** Every later sign-in carries just the stable `sub`, which is why the server keys the lookup on `sub` (as the `AspNetUserLogins` provider key) and persists the email at first contact. Resolving by email would strand every returning user.

  > **The entitlement is not just a plist line.** `Platforms/iOS/Entitlements.plist` and the `CodesignEntitlements` csproj property are necessary but not sufficient — the App ID needs the "Sign In with Apple" capability and the provisioning profile must be regenerated afterwards, or signing succeeds and the runtime request fails with an unhelpful "unknown" error.

  Note the button is hidden off iOS (`IAuthService.IsAppleSignInSupported`, backed by `UnsupportedAppleSignInService`). Do not name the type `AppleSignInAuthenticator` — that collides with `Microsoft.Maui.Authentication.AppleSignInAuthenticator`, which MAUI's implicit usings pull in.

- **`MusicService`/`IMusicService`** — song catalog (`api/music/songs`), likes/dislikes, and stream-count recording with an **offline-durable retry queue** (failed records persist to `IAppPreferenceStore`, flushed on connectivity-restored or a background retry loop).
- **`PlaybackService`/`IPlaybackService`** (~4,000 lines — the largest file in the app) — the platform-neutral playback/queue coordinator: current song/playlist state, repeat/shuffle, preview-limit enforcement for non-subscribers, stream-count triggering, and all failure-recovery decision logic. Delegates actual audio I/O to `IPlatformPlaybackRuntime`, cache/URI resolution to `IAudioCacheService`, and readiness to `IQueuePreparationService`.
- **`QueuePreparationService`** — implements the "is it safe to advance without network" contract (see below).
- **`AudioCacheService`** (non-Android) / **`AndroidMedia3AudioCacheService`** + **`AndroidMedia3CacheProvider`** (Android) — cache/download implementations. Android is authoritative; non-Android is the fallback still needing iOS-parity work.
- **Offline browsing layer** — `IMusicService` and `IPlaylistService` are both resolved as *decorators* in `MauiProgram.cs` (`OfflineAwareMusicService`, `OfflineAwarePlaylistService`) over the concrete `MusicService`/`PlaylistService`. They snapshot to `OfflineSongCatalogStore`/`OfflinePlaylistStore` on a successful live load and, when the API is unreachable, restore that snapshot narrowed to songs whose audio is cached. **Register new music/playlist consumers against the interfaces** and they inherit offline support for free. To find out whether you're looking at live data, the offline cache, or nothing, call `SongCatalogOutcome.For(songs, musicService)` on the result you just awaited — `IMusicService.LastSongsSource`/`LastSongsError` are shared by every caller and a concurrent reload can overwrite them between your await and your read. (`IPlaylistDataSourceReporter.LastPlaylistSource` is the playlist equivalent.) See `PLAYBACK_CACHE_ARCHITECTURE.md` § "Offline Browsing Layer".
- **`IBillingService`** (`GooglePlayBillingService` / `AppStoreBillingService` / `NoBillingService`) — the **server is authoritative** for subscription state: `api/subscription/status` via `AuthService.RefreshUserStatusAsync` is what answers "active trial / converted to paid / neither". The platform store is only a *repair* path, consulted by `TryRestoreBillingAsync` when the server reports no subscription, to catch a purchase the server never learned about (reinstall, or a failed verification POST). Two rules: every entry point awaits `BillingConnectionGate.EnsureConnectedAsync()`, which shares one bounded connection attempt across callers and reconnects after a disconnect — so **startup ordering does not affect correctness**, and nothing can observe a half-built client. And `RestorePurchaseAsync` returning `null` ("the store answered, you own nothing") is **not** the same as a result with `BillingUnavailable` set ("we could not ask"); only the latter is retried, so never collapse the two.
- **`INetworkStatusService`** — two properties, and picking the wrong one is a real bug: `HasNoNetworkAccess` (`NetworkAccess.None`) gates anything that needs the server, while `IsOffline` (`!= Internet`, so also `Unknown`/`ConstrainedInternet`) is pessimistic and belongs only in banner copy and empty states. Subscribers filter with `NetworkStatusChange.AffectsConnectivity(e.PropertyName)`, since the two properties move independently.
- **`ImageCacheService`** + **`ArtworkCachingAudioCacheService`** — artwork is cached at exactly the moment its audio becomes local-ready. Keys come from `StableRemoteAssetKey`, which hashes the blob path plus the server's content version — path-only would never hit, because the server regenerates the Azure SAS query string on every call, and version-less would serve pre-crop artwork forever, because a re-crop overwrites the same blob path in place. **Never bind a raw `*Url` property.** Bind the computed display source matching the surface's size, each of which falls back down to the full-size original and finally to the remote URL:
  - `AlbumArtThumbDisplaySource` — 48-DIP track rows, 36-DIP mini player (320px rendition)
  - `AlbumArtHeroDisplaySource` — 150-DIP cards, 180-DIP player hero (640px rendition)
  - `PersonaImageThumbDisplaySource` — 20/24-DIP artist chips
  - `PersonaImageHeroDisplaySource` — the 120-DIP persona page

  `AlbumArtDisplaySource`/`PersonaImageDisplaySource` still exist as the un-tiered originals but have no production bindings left; prefer a tiered source in new code.

## Playback & cache architecture

Android's playback stack (`Platforms/Android/`) is built on Media3/ExoPlayer: `AndroidMedia3PlaybackRegistry` (owns the singleton `IExoPlayer`/`MediaSession`), `AndroidMedia3PlaybackRuntime` (implements `IPlatformPlaybackRuntime`), `PlaybackMediaSessionService` (the foreground `MediaSessionService`), `AudioVisualizerService` (spectrum/equalizer, driven off `AudioSessionId`).

The full design — including the "sleep-safe" reliability contract, the queue-preparation contract, cache-staleness detection, and failure-recovery rules — is documented in **`PLAYBACK_CACHE_ARCHITECTURE.md`**; read that instead of re-deriving it. Two hard invariants from that doc govern all future playback work:

> The player must never require fresh DNS/network access to advance into any item represented as sleep-safe.

> User-requested pause/stop must never be interpreted as an unexpected playback failure that should restart the queue.

Note the Android/non-Android gap called out in that doc: Android prepares the entire active queue as sleep-safe (`FullQueueSleepSafeContinuityWindow = TimeSpan.Zero`); non-Android platforms still use a 90-minute rolling window (`DefaultSleepSafeContinuityWindow`), an open TODO gated on iOS local-cache trustworthiness.

## In-flight work: perceived-ANR reduction (branch `work/reduce-user-perceived-anr`)

The current branch is a systematic sweep replacing synchronous main-thread I/O and native calls with async/batched/coalesced equivalents. Representative, already-implemented examples to use as templates for future performance fixes:

- **`Services/RollingFileLoggerProvider.cs`** — logging rewritten from synchronous `File.AppendAllText` under a lock (blocking whatever thread logged) to a bounded `Channel<T>` + single background writer batching up to 64 entries / 250ms.
- **`Platforms/Android/AndroidMedia3PlaybackRuntime.cs`** — native ExoPlayer construction changed from eager (in the constructor, on the main thread) to lazy (`EnsureInitializedAsync`, deferred until first playback request).
- **`Services/CoalescedUiUpdateScheduler.cs`** (new) — combines rapidly-arriving update flags into a single dispatched UI update instead of one dispatch per event.
- **`ViewModels/ObservableRangeCollection.cs`** (new) — adds `ReplaceAll()` to raise one `CollectionChanged`/`Reset` instead of one per item during bulk list rebuilds.
- **`Services/AudioVisualizerLifecycleCoordinator.cs`**, **`Services/PlaybackFailureNotificationCoordinator.cs`** (new) — lifecycle-safe visualizer suspend/resume and de-duped failure-toast notifications.

## Reading device logs

The app writes a rolling file log to `/sdcard/Android/data/net.streamtunes.musicsalesapp.maui/files/logs` (`.vscode/pull-latest-runtime-log.ps1` pulls the latest). Before drawing any conclusion from it, know the filter:

> **An absence of log lines proves nothing.** `RollingFileLoggerOptions.CreateDefault()` applies `PlaybackDiagnosticsLoggerFilter.ShouldLog`, which writes **everything at Warning and above**, but below Warning writes **only** the allow-listed diagnostic categories (playback, queue preparation, Media3, media session, visualizer, plus Google Play/App Store billing and `AuthService`). Any other category's `LogInformation` is dropped on the floor.

This has already produced one wrong diagnosis: a successful test subscription left no trace, because every line on that path is Information-level, so a working purchase flow looked identical to one that never ran — while the Warning-level `Disconnected from Google Play Billing` *did* appear and read as a failure. If you are diagnosing a category that is not on that list, add it to `DiagnosticCategoryPrefixes` and rebuild rather than reasoning from silence.

Related gotchas when working with a device:

- Invoke `adb` by its literal full path from the **PowerShell** tool. Git Bash rewrites device paths (`/sdcard/...` becomes `C:/Program Files/Git/sdcard/...`), and invoking adb through a shell variable defeats the permission allowlist.
- Google Play Billing only answers properly for a build Play recognises — same package name and signing key, installed from a track. A locally-signed Release build (plain `dotnet publish` signs with the **debug** keystore) gets errors from every billing query, and Play rejects such an AAB on upload. Use the `create-aab-for-upload` task, which signs with the upload key and also emits `native-debug-symbols.zip` — upload that too, or native ANR traces come back as bare addresses in `libmonosgen-2.0.so`.

## Launch-crash gates (run before publishing)

Three scripts at the repo root install the app and launch/force-close it 3× per target, scoring the logs for startup crashes. They exist because App Store review has rejected releases with "we couldn't test your app because it crashed on startup". They share `test_launch_common.sh` — **the scoring patterns live there, so change them once, not three times**.

| Script | Target | Codegen exercised |
| --- | --- | --- |
| `test_sims.sh` | every iPhone/iPad simulator (`--profile quick` = 6, `full` = 22) | `MtouchRegistrar=static` + `MtouchLink=SdkOnly`, **JIT — not LLVM AOT** |
| `test_device.sh` | the paired iPhone, Release `ios-arm64` | the real App Store codegen, **including LLVM AOT** |
| `test_emulators.sh` | every Android AVD | full AOT, i.e. the real Release codegen |

Two things about this split are easy to get wrong:

- **The iOS simulator gate cannot catch an LLVM-AOT bug** — simulator builds JIT, so `MtouchUseLlvm` is inert. It is still the *only* pre-submission iPad signal, because there is no iPad hardware here, and iPad is the form factor review rejected before. Neither gate substitutes for the other. The Android emulator gate has no such gap: Android Release AOT runs on the emulator.
- **Scoring is calibrated, not naive.** A healthy run contains ~250 lines matching a bare `System.*Exception:` (SignalR reconnects logged at Warning) and ~20 `[Warning]` lines, so neither is a failure. On Android, Google Play Billing errors are filtered out entirely — Play only answers for a build it recognises, so those errors are expected on a locally-signed APK (this is the same trap described under "Reading device logs").

`.vscode/publish-and-upload-ios-appstore-macos.sh` runs the simulator gate before bumping `ApplicationVersion` (so a failure doesn't burn a build number) — `quick` for Test, `full` for Production — plus the device gate for Production. Override with `--skip-smoke-test` / `--skip-device-test`. The device gate signs `ios-arm64` with the Apple **Development** identity, so the publish deletes that output afterwards to stop an incremental build reusing a development-signed artifact.

Tasks: `maui-smoke-test-ios-simulators[-quick]`, `maui-smoke-test-ios-device`, `maui-smoke-test-android-emulators`. Artifacts land in `DeviceLogs/{simulator,device,emulator}-smoke/latest/` — read `summary.txt`, then the failing target's `launch-N.stdio.log`, which is where Mono prints `Unhandled managed exception:` and the managed stack trace.

## Conventions (see `AGENTS.md` for the full list — two rules worth restating since they're easy to violate by habit)

- **No `Models/` folder** — this app has no database entities; DTOs live in `ViewModels/`.
- **No Albums** — Albums are legacy; every song is standalone.
- Every new service/ViewModel/helper needs an NUnit test in `MusicSalesApp.Maui.Tests` (`dotnet test MusicSalesApp.Maui.Tests/`).

## Where to look next

- **`AGENTS.md`** — branch/PR conventions, MVVM patterns, the no-Models/no-Albums rules above, testing mandate.
- **`MAUI_REQUIREMENTS.md`** — original feature spec (auth, flat song library, playlists, Facebook sharing/deep linking). Still broadly accurate, but its API endpoint table is stale versus the actual routes (`api/mobile-auth/*`, `api/music/songs`, `api/subscription/status`, etc.) — trust the code over that table.
- **`PLAYBACK_CACHE_ARCHITECTURE.md`** — authoritative playback/cache design doc, see above.
- **`APPSTORE_SCREENSHOTS.md`** — how to regenerate App Store screenshots from the simulator. Read it before trying: the 6.5-inch slot needs an iPhone *13* Pro Max (the 14 Pro Max is a different resolution), the build must be Production or a test-server banner lands in the listing, the app must be signed in or the player wears a "Preview Only" badge, and Apple rejects the PNGs `simctl` writes because of their alpha channel.
- **`HANDOFF_BACKGROUND_PLAYBACK.md`** — historical Doze/sleep playback-stall investigation that preceded the sleep-safe architecture; superseded but useful for background context.

## Sibling repo

The backend web/API app lives at `../MusicSalesApp` (dual-root VS Code workspace, `MusicSalesApp.local.code-workspace`). This app:

- References `MusicSalesApp.Common` directly from that repo (`../../MusicSalesApp/MusicSalesApp.Common`) — shared constants change in lockstep across both repos.
- Consumes that repo's `api/mobile*`, `api/mobile-auth`, and `api/subscription/*` controllers — see that repo's `CLAUDE.md` for the server-side contract and the mobile API key + JWT auth scheme.
- Its backend URL (`streamtunes.net` vs `davidtest.dev`) is resolved independently on this side via `AppConfig` (above) — the two repos must agree on which environment they're pointed at when testing end-to-end.
