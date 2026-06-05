# Handoff: Android Background Playback Stalls During Sleep

Date: 2026-06-05

## Repo And Logs

- Working repo: `C:\Users\bgmsd\source\repos\MusicSalesApp_Maui`
- Latest pulled device log alias:
  `C:\Users\bgmsd\source\repos\MusicSalesApp_Maui\streamtunes-logs\latest-on-device\streamtunes-latest-device.log`
- Stable latest-log folder:
  `C:\Users\bgmsd\source\repos\MusicSalesApp_Maui\streamtunes-logs\latest-on-device`

## User-Visible Problem

While playing from the Media Library on Android, playback stops or appears to restart early when the phone sleeps. The expected behavior is that pressing Play in Media Library plays the full filtered library queue:

- Unfiltered Media Library: play all songs.
- Filtered Media Library, for example by artist: play all matching songs.
- Repeat should only wrap after the filtered queue has been exhausted.

The user repeatedly reproduced a variant where playback stopped while the phone slept, then resumed after waking the phone. Earlier observations also made it look like repeat wrapped after only a few tracks.

## Latest Log Conclusion

The most recent log did not look like an app crash.

Important observed pattern:

- Playback was active and the app did not show a fresh startup at the failure moment.
- The keep-alive path stayed active.
- Playback reached uncached remote Azure blob URLs while the phone was asleep.
- DNS/network failed with messages like:
  - `Unable to resolve host "highspeedstorageaccount.blob.core.windows.net": No address associated with hostname`
  - similar failures for `streamtunes.net`
- MediaManager/ExoPlayer then reported source errors and failed/buffering states.
- The app's recovery logic advanced forward through additional uncached remote tracks, which also failed while the device network remained unavailable.

Interpretation: this latest failure is primarily a sleep-time network/cache problem, not a managed app crash and not simply a too-short queue.

## Research Findings

Android's Doze behavior matches the log symptoms:

- Android Doze suspends network access.
- Android Doze ignores wake locks.
- A CPU wake lock and Wi-Fi lock can help, but they do not guarantee DNS or remote streaming access while the device is idle.

Relevant docs:

- Android Doze and App Standby:
  https://developer.android.com/training/monitoring-device-state/doze-standby
- Media3 background playback:
  https://developer.android.com/media/media3/session/background-playback
- Foreground service media playback type:
  https://developer.android.com/develop/background-work/services/fgs/service-types
- Media3 downloading media:
  https://developer.android.com/media/media3/exoplayer/downloading-media
- ExoPlayer network/caching related docs:
  https://developer.android.com/media/media3/exoplayer/network-stacks

Practical implication: long-running background audio should use a foreground media playback service and should avoid depending on a fresh remote HTTP stream for every next track while the phone is asleep. The robust pattern is to feed the player local cached/downloaded media whenever possible.

## Current Code Changes

Dirty files at handoff:

- `MusicSalesApp.Maui/Services/PlaybackService.cs`
- `MusicSalesApp.Maui.Tests/Services/PlaybackServiceTests.cs`
- `MusicSalesApp.Maui/MauiProgram.cs`
- `MusicSalesApp.Maui/Platforms/Android/AndroidManifest.xml`
- `MusicSalesApp.Maui/Platforms/Android/PlaybackKeepAliveService.cs`

Key changes in `PlaybackService.cs`:

- Increased background cache warm-ahead from 3 tracks to 12:
  `BackgroundWarmAheadTrackCount = 12`
- Added recovery from remote media item failure when the same track has become locally cached:
  `TryRecoverFailedTrackFromCachedPlaybackUri(...)`
- Keeps playback active through recoverable playlist failures instead of treating the first failed/buffering state as a final stop.
- Adds buffering stall recovery after 8 seconds.
- Adds terminal Paused/Stopped recovery for active playlist playback.
- Suppresses stale native queue rewinds to index 0 after queue rebuild/failure recovery.
- Adds diagnostic logging for delayed position sampler ticks and playback active state changes.

Key changes in Android platform files:

- `AndroidManifest.xml` now explicitly declares both:
  - `android.permission.FOREGROUND_SERVICE`
  - `android.permission.FOREGROUND_SERVICE_MEDIA_PLAYBACK`
- `PlaybackKeepAliveService.cs` now logs whether CPU wake lock and Wi-Fi lock acquisition/release actually happened.

Key tests added or updated:

- `MediaItemFailed_WhenCurrentRemoteTrackIsNowCached_ReplaysCurrentTrackFromCache`
- Active playlist terminal state recovery tests.
- Buffering stall recovery tests.
- Failure-induced rewind suppression tests.
- Queue rebuild captured-start rewind suppression tests.

## Test Status

Focused playback tests passed:

```powershell
dotnet test MusicSalesApp.Maui.Tests/MusicSalesApp.Maui.Tests.csproj --filter PlaybackServiceTests
```

Full MAUI test project passed after the latest changes:

```powershell
dotnet test MusicSalesApp.Maui.Tests/MusicSalesApp.Maui.Tests.csproj
```

Result:

- Failed: 0
- Passed: 754
- Skipped: 0

## What To Check In The Next Device Log

After deploying this build and reproducing with the phone asleep, search the device log for:

- `Playback keep-alive CPU wake lock acquired`
- `Playback keep-alive Wi-Fi lock acquired`
- `Recovering failed remote playlist track from cached playback URI`
- `Audio cache download failed`
- `Unable to resolve host`
- `MediaItemFailed recovery advancing to next track`
- `Buffering stall recovery advancing to next track`
- `Playback diagnostic heartbeat delayed`

Expected improvement:

- If a remote item fails after the cache has finished warming that same track, playback should rebuild and replay it from the local cached URI instead of immediately skipping forward.
- If all upcoming items are uncached and Android has no network/DNS during Doze, failures may still occur. The next larger fix would be more aggressive predownload/offline queue preparation.

## Recommended Next Steps

1. Deploy the current build to the Android device and reproduce the sleep playback scenario.
2. Pull the newest log and check whether cached recovery is triggered.
3. If failures still show uncached remote URLs, consider pre-caching the entire active Media Library filter queue before or shortly after playback starts, not only warming ahead.
4. Inspect the merged Android manifest/build output to verify Plugin.MediaManager's playback service is declared as a foreground media playback service on the final APK/AAB.
5. If Plugin.MediaManager cannot reliably handle modern Android background playback, evaluate moving the Android player path to Media3 `MediaSessionService` plus `DownloadManager`/`CacheDataSource`.

## Notes For Whoever Continues

- Do not interpret every early jump to queue item 0 as app state corruption. Some queue rebuild paths initially start native playback at item 0, then immediately select the captured requested index. The new suppression code is there to ignore stale native callbacks from that window.
- The latest log had no `Playback diagnostic heartbeat delayed` entries, so the most recent failure was not primarily a managed timer suspension symptom.
- `MauiProgram.cs` is already dirty from earlier work. Review its diff before committing, but do not revert it blindly.
