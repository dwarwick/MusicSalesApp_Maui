# Playback And Sleep-Safe Cache Architecture

This file documents the Android playback/cache migration and the shared architecture that future platform work should preserve. It is intended as a handoff for later Codex sessions, especially for verifying and improving iOS playback parity.

## Short Version

Android playback was moved away from `Plugin.MediaManager` and onto a Media3 based runtime:

- Shared app playback state remains in `PlaybackService`.
- Platform audio output is hidden behind `IPlatformPlaybackRuntime`.
- Queue readiness is explicit through `QueuePreparationResult`.
- Android playback uses Media3 `ExoPlayer`, `MediaSessionService`, and one shared `SimpleCache`.
- Android sleep-safe playback is defined as local-ready playback. The app must not depend on fresh DNS/network access to advance into an item it has represented as sleep-safe.
- Android now prepares the full active queue for sleep-safe playback, subject to the user cache limit and device free-space reserve.
- If a remote item fails during a network/DNS constrained period, the app preserves queue position unless the next item is already local-ready.
- Android now distinguishes user-requested media-control pause/stop from unexpected terminal playback states. Notification stop clears the native Media3 queue, releases the MediaSession, and removes the foreground playback notification.
- Swiping the app from Android recents while audio is playing is expected to keep playback alive through the foreground media notification. The user stops background playback through the app or notification controls.
- iOS/Mac/Windows still use the non-Android runtime for now, but should reuse the shared abstractions and readiness contract.

## Why This Work Was Done

The original Android failure was not simply a managed exception or a queue-length bug. It was caused by the intersection of:

- Android background and Doze network restrictions.
- Remote streaming URLs that require DNS/network access.
- Queue advance behavior that could keep trying remote-only items.
- A playback abstraction that did not expose enough platform control for MediaSession, cache, audio session ID, and deterministic queue readiness.

Foreground media playback helps Android keep audio eligible to continue, but it does not make remote network access reliable after the phone sleeps. The reliable fix is to make the active playback order local-ready before sleep-dependent transitions happen.

## Reliability Contract

This is the core contract for sleep-safe playback:

1. The player must never require fresh DNS/network access to advance into any item represented as sleep-safe.
2. Queue preparation must produce an explicit readiness result. Warm-ahead alone is not a guarantee.
3. During playback failure:
   - If the current item has a local cache entry, retry the current item from cache.
   - Else, if the next item is local-ready, advance to it.
   - Else, preserve the current queue position and enter `Preparing` or `WaitingForNetwork`.
   - Never cascade-skip through remote-only tracks while network/DNS is failing.
4. Full filtered-library reliability requires the entire active filtered playback order to be local-ready. Anything less is only a continuity window.

## Shared Architecture

### `PlaybackService`

File: `MusicSalesApp.Maui/Services/PlaybackService.cs`

`PlaybackService` remains the singleton app-level playback coordinator. It is intentionally platform-neutral. It owns:

- Current song and playlist state.
- Repeat and shuffle behavior.
- Preview-limit behavior for non-subscribed users.
- Stream-count tracking.
- Current position and duration UI state.
- Queue building and queue reloads.
- Failure recovery decisions.
- Queue preparation status exposed through:
  - `PreparationState`
  - `LastQueuePreparationResult`

It no longer talks directly to `Plugin.MediaManager` on Android. Instead, it calls:

- `IPlatformPlaybackRuntime` for actual playback.
- `IAudioCacheService` / `ITrackCacheService` for cache status and playback URI resolution.
- `IQueuePreparationService` for deterministic readiness.

### Runtime Abstraction

Files:

- `MusicSalesApp.Maui/Services/IPlatformPlaybackRuntime.cs`
- `MusicSalesApp.Maui/Services/PlaybackRuntimeTypes.cs`

`IPlatformPlaybackRuntime` is the platform audio boundary. It exposes:

- Runtime state: `Stopped`, `Playing`, `Paused`, `Buffering`, `Failed`.
- Queue operations: play, pause, stop, next, previous, play queue item, seek.
- Runtime events: state changed, media item changed, position changed, item finished, item failed.
- `AudioSessionId`, used by the Android visualizer.
- Repeat and shuffle modes.

`PlaybackRuntimeStateChangedEventArgs` carries both:

- `State`
- `Reason`

Current reasons:

- `Unknown`
- `UserRequest`

This distinction matters. A terminal state caused by a notification/media-control stop is intentional and must not trigger playlist recovery. A terminal state with `Unknown` reason may still be treated as a transient platform stall and confirmed/recovered by `PlaybackService`.

`IIndexedQueuePlaybackRuntime` is an optional extension used by runtimes that can start a native queue at a specific index without first starting item 0 and seeking later. Android Media3 implements this.

`PlaybackMediaItem` carries the runtime payload:

- `MediaUri`
- `SongId`
- `StableCacheKey`
- metadata such as title, artist, image URI
- `IsLocal`
- `IsSleepSafe`

`IsSleepSafe` must mean the item is locally playable. It is not a statement that a remote URL is likely to work.

### Queue Preparation Contract

Files:

- `MusicSalesApp.Maui/Services/QueuePreparationTypes.cs`
- `MusicSalesApp.Maui/Services/QueuePreparationService.cs`

`QueuePreparationResult` reports:

- `CurrentTrackReady`
- `ReadyThroughQueueIndex`
- `ReadyThroughDuration`
- `NotReadyItems`
- `Mode`
- `FailureReason`

Modes:

- `Normal`: start quickly. Prepare the current track and warm upcoming items in the background.
- `SleepSafe`: only report reliability through the fully local-ready portion of the active playback order.

On Android, `PlaybackService.GetSleepSafeContinuityWindow()` returns `QueuePreparationService.FullQueueSleepSafeContinuityWindow`, which is `TimeSpan.Zero`. In this implementation, zero means prepare through the end of the active queue. That is why the Android release test could report `ReadyThroughQueueIndex=121` for a 122 item queue.

On non-Android, the current code still uses `QueuePreparationService.DefaultSleepSafeContinuityWindow`, which is 90 minutes. That is not full-queue parity.

### Cache Abstraction

Files:

- `MusicSalesApp.Maui/Services/QueuePreparationTypes.cs`
- `MusicSalesApp.Maui/Services/AudioCacheService.cs`
- `MusicSalesApp.Maui/Services/AudioCacheKeyHelper.cs`
- `MusicSalesApp.Maui/Platforms/Android/AndroidMedia3AudioCacheService.cs`

`ITrackCacheService` exposes:

- `GetStableCacheKey`
- `GetCacheStatus`
- `EnsureCachedAsync`
- `PinActiveQueue`

`IAudioCacheService` extends it with:

- `GetImmediatePlaybackUri`
- `ResolvePlaybackUriAsync`

Stable cache identity is critical. The stable key is based on:

- song ID
- stable path metadata from the remote URI

It intentionally ignores the signed URL query string. A changed SAS token must not invalidate an already downloaded track. The test `SignedUrlChanges_ButStableCacheKeyStillFindsDownloadedTrack` covers this for the generic cache.

Pin scopes:

- `TemporaryWarm`: removable warm-ahead content.
- `ActiveQueue`: protected content for the current queue.
- `Offline`: durable user/offline content reserved for future broader offline behavior.

## Android Implementation

### DI Registration

File: `MusicSalesApp.Maui/MauiProgram.cs`

Android registrations:

- `IAudioCacheService` -> `AndroidMedia3AudioCacheService`
- `ITrackCacheService` -> same `IAudioCacheService`
- `IQueuePreparationService` -> `QueuePreparationService`
- `IPlatformPlaybackRuntime` -> `AndroidMedia3PlaybackRuntime`
- `IPlaybackKeepAliveService` -> Android `PlaybackKeepAliveService`
- `IAudioVisualizerService` -> Android `AudioVisualizerService`

Non-Android registrations:

- `IAudioCacheService` -> generic `AudioCacheService`
- `IPlatformPlaybackRuntime` -> `MediaManagerPlaybackRuntime`
- `Plugin.MediaManager` remains referenced only for non-Android platforms.

### Media3 Runtime

Files:

- `MusicSalesApp.Maui/Platforms/Android/AndroidMedia3PlaybackRuntime.cs`
- `MusicSalesApp.Maui/Platforms/Android/AndroidMedia3PlaybackRegistry.cs`
- `MusicSalesApp.Maui/Platforms/Android/PlaybackMediaSessionService.cs`

`AndroidMedia3PlaybackRuntime` wraps Media3 `ExoPlayer` and implements `IPlatformPlaybackRuntime` plus `IIndexedQueuePlaybackRuntime`.

Responsibilities:

- Start/ensure the `PlaybackMediaSessionService`.
- Create Media3 media items from `PlaybackMediaItem`.
- Set Media3 `MediaId` and custom cache key to `StableCacheKey`.
- Set metadata for notifications and lock screen.
- Publish playback state, media item changes, position, finish, and failure events back to `PlaybackService`.
- Expose `AudioSessionId` for `AudioVisualizerService`.
- Suppress duplicate or startup false-positive finish events.
- Map Media3 errors into `MediaItemFailed` events.
- Translate Media3 `PlayWhenReady` user/remote stop reasons into `PlaybackRuntimeStateChangeReason.UserRequest`.
- Suppress user-request markers around app-driven commands such as queue rebuilds and internal stop cleanup.
- Clear the native queue and request MediaSession service teardown from `StopAsync`.

`AndroidMedia3PlaybackRegistry` owns the singleton `ExoPlayer` and `MediaSession`.

Important design choice:

- The player is built with `AndroidMedia3CacheProvider.GetMediaSourceFactory(context)`.
- This means all playback goes through `CacheDataSource.Factory`.
- Even when the media URI is remote, Media3 can satisfy reads from the shared cache by stable cache key.

`PlaybackMediaSessionService` is declared with:

- `ForegroundServiceType = ForegroundService.TypeMediaPlayback`
- intent action `androidx.media3.session.MediaSessionService`

It releases the MediaSession in `OnDestroy`.

It also exposes an internal explicit stop path used by `AndroidMedia3PlaybackRuntime.StopAsync`:

1. Remove the current MediaSession from the service.
2. Release the singleton MediaSession through `AndroidMedia3PlaybackRegistry`.
3. Stop foreground mode with notification removal.
4. Stop the service.

That explicit path is what prevents a stopped notification from disappearing briefly and then being recreated by SystemUI with stale queue metadata.

### Android Notification And Recents Lifecycle

Android foreground media playback has a different lifecycle than a normal foreground app screen.

Expected behavior:

- If audio is playing and the user swipes StreamTunes from recents, playback should continue and the media notification should remain. This is normal for a background music app.
- If audio is not playing and the user swipes StreamTunes from recents, the app can disappear without a playback notification.
- If the user pauses from the notification, `PlaybackService` treats the terminal state as intentional and does not run playlist recovery. The session may remain resumable.
- If the user stops from the notification, `PlaybackService` treats the terminal state as intentional, disables recovery, calls runtime stop cleanup, clears the native Media3 queue, and tears down the foreground MediaSession service.

The hard lifecycle invariant is:

```text
User-requested pause/stop must never be interpreted as an unexpected playback failure that should restart the queue.
```

Implementation notes:

- Media3 reports user and remote media-control stops through `OnPlayWhenReadyChanged`.
- Android runtime maps Media3 user/remote terminal reasons to `PlaybackRuntimeStateChangeReason.UserRequest`.
- `PlaybackService.ApplyUserRequestedTerminalPlaybackState` cancels pending recovery and sets `IsPlaying=false`.
- For `Stopped`, `PlaybackService` calls `_playbackRuntime.StopAsync()` once and suppresses duplicate cleanup for a short window.
- Android runtime suppresses stale user-request markers during app-driven stop cleanup, so its own `StopAsync` event does not reenter the user-stop path.
- `PlaybackMediaSessionService.RequestStop` removes foreground notification state and releases the MediaSession.

### Media3 Cache

File: `MusicSalesApp.Maui/Platforms/Android/AndroidMedia3CacheProvider.cs`

Android uses one shared Media3 download/playback cache:

```csharp
SimpleCache(downloadDirectory, NoOpCacheEvictor, StandaloneDatabaseProvider)
```

Current directory:

```text
context.CacheDir/media3-download-cache
```

The same cache instance is used by:

- `DownloadManager`
- `CacheDataSource.Factory`
- `DefaultMediaSourceFactory`
- cache cleanup
- cache size and storage checks

Important details:

- `NoOpCacheEvictor` is used because sleep-safe content must not be removed automatically by LRU eviction.
- Cleanup is explicit and app-policy driven.
- `DownloadManager.MaxParallelDownloads = 2`.
- `DownloadManager.MinRetryCount = 3`.
- The Media3 package versions are pinned through `AndroidXMedia3Version` in the csproj.

### Android Cache Readiness

File: `MusicSalesApp.Maui/Platforms/Android/AndroidMedia3AudioCacheService.cs`

This service is Android's `IAudioCacheService`.

It queues Media3 downloads and polls `DownloadManager.DownloadIndex`, but it does not trust the download index alone. Readiness is true only when:

1. The Media3 download state is `Download.StateCompleted`.
2. The underlying cache spans are present for the content length:

```csharp
cache.IsCached(stableCacheKey, 0, contentLength)
```

This matters because Android's "Clear cache" can remove cache files while another Media3 database/index survives. A stale index may say "completed" even when the bytes are gone. The service now logs and removes stale completed download records before re-downloading.

Relevant log messages:

- `Media3 download queued`
- `Media3 download state changed`
- `Media3 download index reports completed content, but local cache spans are incomplete`
- `Removing stale Media3 completed download because cache files are missing or incomplete`
- `Queue preparation completed`

### Android Manifest And Play Console Impact

Files:

- `MusicSalesApp.Maui/Platforms/Android/AndroidManifest.xml`
- `MusicSalesApp.Maui/Platforms/Android/PlaybackMediaSessionService.cs`

Android declares:

- `FOREGROUND_SERVICE`
- `FOREGROUND_SERVICE_MEDIA_PLAYBACK`
- network and microphone permissions used elsewhere in the app.

Android does not declare:

- `FOREGROUND_SERVICE_DATA_SYNC`
- a data-sync foreground service
- `MusicDownloadService`

This was intentional. The app prepares the active queue while the app is active and while playback is active, but it does not claim a separate foreground data-sync task for Play Console policy purposes. If queue preparation cannot finish before the user sleeps, the reliability contract still protects the user: the app may only advance through local-ready content and must wait rather than cascade through remote-only items.

## Playback Flow

### Single Song

`PlaybackService.PlaySong(song)`:

1. Sets shared state immediately.
2. Creates one `PlaybackMediaItem` using `IAudioCacheService.GetImmediatePlaybackUri(song)`.
3. Starts playback through `IPlatformPlaybackRuntime.PlayAsync`.
4. Starts queue preparation in `Normal` mode for the single item.
5. Starts background warm caching.

Normal mode is optimized for quick start, not full sleep guarantee.

### Playlist / Filtered Queue

`PlaybackService.SetPlaylist(songs, startIndex)`:

1. Stores the playlist in actual playback order.
2. If shuffle is enabled, the playlist order is rebuilt before playback and preparation.
3. Starts queue preparation in `SleepSafe` mode.
4. Builds runtime queue items using immediate cache status.
5. Starts the platform runtime queue at the selected index.
6. Starts background warm caching of the next 12 tracks.

On Android, SleepSafe preparation currently targets the full active queue. That means a filtered artist queue or shuffled queue should be prepared in the same order the player will use, not in library sort order.

### Background Warm-Ahead

`PlaybackService` still has warm-ahead behavior:

- `BackgroundWarmAheadTrackCount = 12`
- `QueueCacheResolutionConcurrency = 3`

This is a convenience path for quicker starts and normal playback. It is not the sleep-safe guarantee. The guarantee comes from `QueuePreparationResult` and `IsLocalReady`.

## Failure Recovery

Failure recovery lives in `PlaybackService`.

When the platform runtime raises `MediaItemFailed`:

1. Resolve the failed queue index conservatively.
2. Schedule immediate/delayed recovery depending on runtime state.
3. Try to replay the current failed track if it is now local-ready.
4. Resolve the next sequential track.
5. If there is no next track and repeat is off, stop.
6. If the next track is not local-ready, enter `WaitingForNetwork` and preserve queue position.
7. If the next track is local-ready, reload/advance the queue.

The hard invariant is:

```text
Do not advance from a failed remote item into another remote-only item during constrained playback.
```

This prevents the old bad behavior where the player could burn through a queue with repeated host-resolution errors.

### User-Requested Stop Is Not Failure Recovery

Android notification and media-button controls can produce terminal runtime states while the shared service still has a current playlist. Those states are not failures when their reason is `UserRequest`.

For user-requested terminal states:

- Cancel pending playlist advance.
- Cancel pending playback requests.
- Cancel pending terminal-state confirmation.
- Set `IsPlaying=false`.
- Do not call terminal playlist recovery.
- If the state is `Stopped`, cancel queue preparation and call runtime stop cleanup.

For unknown terminal states:

- Keep the existing transient stop confirmation behavior.
- If playback position advances, ignore the transient terminal state.
- If a playlist is active and the state appears to be an unexpected stop/stall, recovery can still reload the queue.

This preserves both requirements: real stalls can recover, while explicit user stops stay stopped.

## User Cache Settings

Files:

- `MusicSalesApp.Maui/Services/OfflineCacheSettingsService.cs`
- `MusicSalesApp.Maui/ViewModels/ConfigViewModel.cs`
- `MusicSalesApp.Maui/Views/ConfigPage.xaml`
- `MusicSalesApp.Maui/Services/MobilePreferenceKeys.cs`

The Config page exposes a user setting for maximum potential offline/prepared audio storage:

- Minimum: 100 MB
- Default: 1 GB
- Maximum: 5 GB
- Device free-space reserve: 1 GB

The wording on the page is important. The configured value is a maximum potential cache size, not a promise that the app will always consume exactly that much. The free-space reserve can reduce actual usage.

If the configured limit or reserve is reached:

- The cache service skips additional downloads.
- Queue preparation reports not-ready items.
- Sleep-safe playback should only continue through already local-ready items.

## Storage And Song Protection

The prepared audio cache is in the app's private Android app storage. Normal users cannot browse to this cache through the regular file picker or media library. Clearing app cache/data can remove it.

This is not DRM. A rooted device, debug/instrumented environment, or OS-level compromise can access private app storage. The current design prevents ordinary casual access, not malicious extraction on rooted phones.

If stronger protection is required later, that is a separate DRM/encryption/licensing project. It should not be confused with this sleep-safe cache work.

## Validation Already Performed

The Android release validation in this migration included:

- `dotnet test MusicSalesApp.Maui.Tests\MusicSalesApp.Maui.Tests.csproj`
- Android release build/package validation for `net10.0-android`
- Manifest/package checks:
  - no `FOREGROUND_SERVICE_DATA_SYNC`
  - no `dataSync` service
  - no `MusicDownloadService`
  - no Android `Plugin.MediaManager` playback service
  - `PlaybackMediaSessionService` uses `mediaPlayback`
- Real phone playback with the screen asleep / Dozing.
- Cold data reset before install to wipe both cache files and Media3 download index.
- 122 item queue prepared.
- Logs showed full queue readiness:
  - `QueueCount=122`
  - `CurrentTrackReady=True`
  - `ReadyThroughQueueIndex=121`
  - `NotReadyCount=0`
- Follow-up sleep sample showed:
  - playback still `PLAYING`
  - queue size `122`
  - no StreamTunes playback/DNS errors
  - no new downloads after readiness
  - no failed downloads
- Notification/recents lifecycle validation:
  - swiping the app from recents while audio was playing kept playback alive
  - notification stop did not restart playback
  - native Media3 queue was cleared after stop
  - `dumpsys media_session` no longer listed a StreamTunes MediaSession after stop cleanup
  - `dumpsys activity services net.streamtunes.musicsalesapp.maui` no longer listed `PlaybackMediaSessionService` after stop cleanup
  - duplicate internal stop-cleanup events were suppressed

There were DNS errors from other apps on the device during the test. Those were not StreamTunes playback failures.

## Tests To Preserve

Key tests added or updated during this work include:

- `SignedUrlChanges_ButStableCacheKeyStillFindsDownloadedTrack`
- `QueuePreparationResult_ReportsReadyThroughIndexAndDuration`
- `DownloadFailure_PreservesCurrentReadinessAndReportsNotReadyItems`
- `SleepSafe_WithZeroContinuityWindow_PreparesThroughQueueEnd`
- `SleepNetworkFailure_DoesNotAdvanceToRemoteOnlyTrack`
- `MediaItemFailed_WhenCurrentRemoteTrackIsNowCached_ReplaysCurrentTrackFromCache`
- `SetPlaylist_WhenCachedPlaybackUriIsAvailableForCurrentSong_UsesLocalQueueItemForStartTrack`
- `SetPlaylist_WhenCachedPlaybackUrisAreAvailableForUpcomingTracks_UsesLocalQueueItemsForNativeHandoff`
- `SetPlaylist_WhenRuntimeSupportsIndexedQueueStart_PassesStartIndexWithoutSeparateSeek`
- `MediaManagerTerminalState_UserRequest_WithActivePlaylist_DoesNotRecover`
- `MediaManagerStopped_UserRequest_StopCleanupDoesNotReenterFromRuntimeStopEvent`
- shuffle/repeat/native-queue recovery tests in `PlaybackServiceTests`

When extending iOS, add tests before relying on manual device results.

## iOS Parity Guidance

iOS should reuse the shared architecture, but it should not copy Android Media3 code. Media3 is Android-specific.

### Reuse These Shared Pieces

iOS should keep using or extend:

- `PlaybackService`
- `IPlatformPlaybackRuntime`
- `PlaybackMediaItem`
- `PlaybackRuntimeState`
- `ITrackCacheService`
- `IAudioCacheService`
- `IQueuePreparationService`
- `QueuePreparationResult`
- `AudioCacheKeyHelper`
- `OfflineCacheSettingsService`
- `ConfigViewModel` / `ConfigPage`

The shared service already knows how to:

- build queues in actual playback order
- enforce preview limits
- preserve queue position on unsafe failure recovery
- expose readiness state
- apply repeat/shuffle behavior

### Current iOS State

As of this document, iOS remains on the non-Android `MediaManagerPlaybackRuntime` plus generic `AudioCacheService`.

Important differences from Android:

- iOS currently uses the non-Android 90 minute SleepSafe continuity window.
- iOS does not currently have the Android full-queue sleep-safe policy.
- The generic file cache does not use Media3 and does not have Media3 cache-span verification.
- iOS background playback behavior must be verified separately with AVAudioSession/background modes.

### Recommended iOS Runtime Direction

For production-quality iOS parity, implement an iOS native runtime behind `IPlatformPlaybackRuntime`, likely using:

- `AVPlayer` or `AVQueuePlayer`
- `AVAudioSession`
- iOS background audio mode
- `MPNowPlayingInfoCenter`
- `MPRemoteCommandCenter`
- interruption handling
- route-change handling
- lock-screen metadata and controls
- remote-command pause and stop semantics
- explicit queue index reporting
- failure events mapped into `PlaybackMediaItemFailedEventArgs`

The iOS runtime should support indexed queue start if possible by implementing `IIndexedQueuePlaybackRuntime`.

### Recommended iOS Cache Direction

iOS should implement or harden `IAudioCacheService` so that `GetCacheStatus(song).IsLocalReady` means a complete local file is present and playable.

Recommended behavior:

- Keep the same stable cache key strategy.
- Store prepared files in app-private storage.
- Exclude cache content from iCloud backup if stored outside the system Caches directory.
- Validate local file presence and non-zero length at minimum.
- Prefer validating expected content length when available.
- Do not let stale metadata claim readiness when files are missing.
- Respect the same configured cache limit and free-space reserve.
- Pin active queue items and avoid deleting them during cleanup.

For true iOS parity, change iOS sleep-safe preparation to full active queue only after iOS local-readiness is trustworthy:

```csharp
#if ANDROID || IOS
    return QueuePreparationService.FullQueueSleepSafeContinuityWindow;
#else
    return QueuePreparationService.DefaultSleepSafeContinuityWindow;
#endif
```

Do not make that change unless iOS playback actually reads local files for local-ready items and failure recovery is verified.

### iOS Manual Verification Checklist

Use a real iPhone if possible. Simulators do not perfectly represent background audio behavior.

1. Clear app data or uninstall/reinstall.
2. Start a filtered queue with enough songs to exercise several transitions.
3. Confirm the queue order in logs matches the actual playback order, including shuffle if enabled.
4. Confirm queue preparation logs report:
   - `CurrentTrackReady=True`
   - `ReadyThroughQueueIndex` covering the intended sleep-safe range
   - `NotReadyCount=0` for the intended range
5. Lock the phone and let playback continue.
6. Disable network after readiness, for example airplane mode after the prepared range is ready.
7. Confirm local-ready tracks advance without network.
8. Confirm a remote-only successor does not get skipped into repeatedly. It should wait at the correct queue position.
9. Confirm lock-screen metadata and controls update.
10. Confirm notification/remote-control pause does not trigger recovery/restart.
11. Confirm notification/remote-control stop releases playback resources and does not resurrect playback.
12. Confirm swiping/closing the foreground app while audio is playing follows the intended iOS background-audio behavior.
13. Confirm preview-limit behavior while logged out still advances at 60 seconds without locking up the UI.
14. Confirm repeat does not wrap early before queue exhaustion.
15. Confirm shuffle prepares actual shuffled playback order, not library sort order.
16. Confirm cache limit and free-space reserve are respected.
17. Confirm app relaunch does not falsely claim readiness if files were removed.

### iOS Tests To Add

Add iOS/runtime-neutral tests mirroring Android behavior:

- iOS local-ready current item uses a file URI.
- iOS signed URL changes still hit the same cache key.
- iOS queue preparation reports ready-through index and duration.
- iOS sleep/network failure does not advance into a remote-only item.
- iOS cached successor advances.
- iOS uncached successor waits.
- iOS shuffle prepares actual playback order.
- iOS repeat does not wrap before queue exhaustion.
- iOS runtime updates metadata for lock screen.
- iOS runtime releases player/session resources.
- iOS user-requested pause/stop does not trigger playlist recovery.
- iOS stop command tears down notification/remote-command state according to platform expectations.

## Important Log Lines

Useful Android logcat patterns:

```text
StreamTunes
Queue preparation completed
Media3 download queued
Media3 download state changed
Media3 download index reports completed content
Removing stale Media3 completed download
MediaItemFailed received
Automatic recovery preserved queue position
WaitingForNetwork
Media3 player error
Playback runtime state change received
Reason=UserRequest
Playback runtime.Stop from user-requested terminal state
Media3 playback service stop requested
```

Useful adb checks:

```powershell
adb shell dumpsys power
adb shell dumpsys media_session
adb logcat -d -v time
adb shell dumpsys package net.streamtunes.musicsalesapp.maui
```

Useful Android release gate:

```powershell
adb shell dumpsys deviceidle force-idle
```

After force-idle, local-ready tracks should continue. Remote-only failures should wait, not skip through the queue.

## Known Tradeoffs

### No Data-Sync Foreground Service

We removed the dedicated Media3 `DownloadService` / data-sync foreground service path. This reduces Play Console policy surface and avoids having to show a persistent "preparing playback" data-sync notification.

Tradeoff:

- Downloads are most reliable while the app/playback process is active.
- If the user sleeps the phone before preparation completes, Android may restrict network progress.

The reliability contract handles that by never pretending remote-only items are sleep-safe.

### Full Queue Cache Can Use Storage

Full queue preparation can consume substantial storage. The Config page limits this to a user-configured maximum and a device free-space reserve.

If a 100+ song queue exceeds the limit, the correct behavior is not to claim full sleep safety. The queue preparation result should show where readiness stops and failure recovery should wait at the first non-ready item if network is constrained.

### Cache Directory Is Clearable

The Android cache is in `context.CacheDir`. Android can clear it. The code now detects stale Media3 download-index state and re-downloads instead of falsely claiming readiness.

## Files Most Relevant To This Architecture

Shared:

- `MusicSalesApp.Maui/Services/PlaybackService.cs`
- `MusicSalesApp.Maui/Services/IPlaybackService.cs`
- `MusicSalesApp.Maui/Services/IPlatformPlaybackRuntime.cs`
- `MusicSalesApp.Maui/Services/PlaybackRuntimeTypes.cs`
- `MusicSalesApp.Maui/Services/QueuePreparationTypes.cs`
- `MusicSalesApp.Maui/Services/QueuePreparationService.cs`
- `MusicSalesApp.Maui/Services/AudioCacheService.cs`
- `MusicSalesApp.Maui/Services/AudioCacheKeyHelper.cs`
- `MusicSalesApp.Maui/Services/OfflineCacheSettingsService.cs`
- `MusicSalesApp.Maui/Services/MobilePreferenceKeys.cs`
- `MusicSalesApp.Maui/ViewModels/ConfigViewModel.cs`
- `MusicSalesApp.Maui/Views/ConfigPage.xaml`

Android:

- `MusicSalesApp.Maui/Platforms/Android/AndroidMedia3PlaybackRuntime.cs`
- `MusicSalesApp.Maui/Platforms/Android/AndroidMedia3PlaybackRegistry.cs`
- `MusicSalesApp.Maui/Platforms/Android/PlaybackMediaSessionService.cs`
- `MusicSalesApp.Maui/Platforms/Android/AndroidMedia3CacheProvider.cs`
- `MusicSalesApp.Maui/Platforms/Android/AndroidMedia3AudioCacheService.cs`
- `MusicSalesApp.Maui/Platforms/Android/AudioVisualizerService.cs`
- `MusicSalesApp.Maui/Platforms/Android/AndroidManifest.xml`

Non-Android:

- `MusicSalesApp.Maui/Services/MediaManagerPlaybackRuntime.cs`

Tests:

- `MusicSalesApp.Maui.Tests/Services/AudioCacheServiceTests.cs`
- `MusicSalesApp.Maui.Tests/Services/QueuePreparationServiceTests.cs`
- `MusicSalesApp.Maui.Tests/Services/PlaybackServiceTests.cs`

## Final Guidance For Future Work

The main idea to preserve is deterministic readiness. Do not let any platform represent a track or queue range as sleep-safe unless the next item is already local-playable.

For Android, the key win was using one Media3 cache shared by downloads and playback, then validating actual cache spans before reporting readiness.

For iOS, the key win will be to reuse the same shared contract while replacing only the platform pieces: native playback runtime, native/background audio integration, and a trustworthy local cache implementation.
