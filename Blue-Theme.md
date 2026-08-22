# Blue Theme — handoff

State of the MAUI app after porting the web design system, synchronized lyrics, the player and
artist page rebuilds, and tablet layouts. Written for picking the work up on macOS to check iOS.

Branch: `work/maui-synchronized-lyrics` (branches directly off `master`; contains
`work/port-web-design-system` as an ancestor, so it is the only MAUI branch to pull).
Version: **1.0.107 / 107**. Master is at 1.0.105.

Everything below was verified on Android — a phone and the `Tablet_API_35` emulator. **None of it
has been run on iOS or iPadOS.** That is the job on the Mac.

---

## What changed

### Design system
`Resources/Styles/Colors.xaml`, `Tokens.xaml`, `AppColors.cs` hold the tokens ported from the web's
`tokens.css`. Four invariants are written up in `AGENTS.md`, including the verification greps.

### Synchronized lyrics
Word-level highlighting on both players and the library cards, cached for offline alongside audio.

The position feed is **1 Hz** — there is no sub-second source on any platform — so `LyricsClock`
interpolates between anchors and hard-resyncs past a 1s discrepancy. `LyricsTimeline` never consults
`Ends`, so a word stays lit through the gap until the next one starts; consulting `Ends` makes the
gaps between lines flicker. Both are pure and unit-tested.

Server side is the Blazor repo's `work/mobile-lyrics-and-persona-website`, which adds
`PersonaWebsiteUrl`, `LyricsTimingsPath` and `LyricsVersion` to the mobile DTOs. **Already deployed
to davidtest.dev**, so Debug builds see them.

### Players and artist page
Both players and the artist page were rebuilt to match the web: a hero identity card, bordered panels
with uppercase captions, a Lyrics/Art segmented switch, and the same section order the web uses
(hero → songs → stage → about). The artist page's track list marks the playing row and swaps its
number for an equalizer, as the web's number cell does.

The Lyrics/Art switch is `Border` + `Label`, not `Button`: a `UIButton` whose layer corner radius is
the pill `999` does not paint its fill on iOS, which left the active segment's near-black label
sitting straight on the panel and invisible. The genre chip beside it is the same Border-and-Label
recipe with the same shape and has always drawn correctly, so the shape was never the problem.

The library's filter pills and the Log In / Register / Subscribe CTAs were the same fault. All three
are fixed, and the cause was **measured** rather than reasoned about — three controls side by side on
an iPhone 17 Pro simulator:

| Control | Radius | Result |
| --- | --- | --- |
| `Button` | `RadiusPillInt` 999 | **nothing renders** — no fill, no label |
| `Button` | `RadiusSmInt` 8 | correct |
| `Border` | `RoundRectangle 999` | correct capsule |

So it is the radius exceeding the control's own bounds, not `Button` backgrounds on iOS generally,
and `Border` is immune because it clamps the shape to its bounds. The label survives on a dark
surface (the fill is what goes missing), which is why this read as "no fill" on a phone and as
"completely invisible" in light theme, where the label is white on white.

Two fixes, chosen per context. Chrome that was already being restyled — the Lyrics/Art switch and the
filter pills — became `Border` + `Label`. The CTAs stayed `Button` and took a new
`RadiusPillControlInt` (22, half of `ControlHeight`): they carry `IsEnabled` bindings and are the
app's primary actions, so the disabled state and the button accessibility role are worth keeping.
Written up as invariant 5 in `AGENTS.md` with a grep, because Android clamps and looks correct either
way — this is invisible on the platform most of the work happens on.

### Playback correctness
Two real bugs, both fixed and both worth knowing about because the diagnosis was misleading:

1. **The players reported a song they were not playing.** Leaving the home page's featured queue for
   the artist queue swaps one two-song queue for another where the same song sits at a different
   index. `SetMediaItems(items, startIndex, position)` left the player on index 0, and the correction
   seeked by INDEX across two different orderings — dragging playback onto the wrong song while every
   label kept naming the right one. Now `PlaybackQueueAlignment` decides by media id: a player already
   on the right song is left alone whatever its index. 13 tests.

2. **The queue check compared counts.** Two different queues of the same length compared equal, so a
   submission that left the old order in place looked applied. Also fixed by identity.

3. **A tapped song played forever, or played the next song — depending on the platform.** Tapping a
   featured song runs two queue operations: the home page starts the featured queue, then
   `SongPlayerViewModel` deliberately shrinks the active queue to the one song its page shows. Two
   separate faults met there. `SetPlaylist` pinned the runtime to `RepeatMode.All` regardless of
   `IsRepeatEnabled`, on the theory that the native player needed it to advance between tracks — it
   does not, `Off` advances and stops at the end, which is the case `EnsurePlaylistContinuesAsync`
   was written for and could never reach. So the one-song queue **wrapped**: Android played the song
   forever, and the repeat button could not turn repeat off while any queue was active. Meanwhile
   `MediaManagerPlaybackRuntime` implemented neither queue interface, so
   `ReplaceNativeQueuePreservingCurrentPlayback` logged and returned and the shrink never reached
   the player at all — iOS kept the stale featured queue underneath and advanced into it. Both are
   fixed; repeat now follows the button, and the Apple runtime implements
   `IQueueReplacementPlaybackRuntime`, skipping the rebuild by media id when the player already
   holds the queue so that opening the page for the playing song does not restart it mid-track.
   Both platforms now match the web's `SongPlayerInteractive.AudioEnded`: play once, stop, and
   restart only with repeat on.

> **Diagnostic trap.** Every `CurrentItem` / `Native*` field in the playback snapshots is
> `_queue.Current` — the runtime's own item list indexed by the player's index. It reports what the
> app believes no matter what the player holds, so an order mismatch looks like agreement. The
> player's own truth is `DurationMs`, and now `PlayerMediaId` / `PlayerQueueIds`, which were added
> for exactly this reason. Read those first.

### Tablet layouts
Content caps at 1100dp and centres; both players and the artist page go two-column at **992dp**,
which is the web's own breakpoint (`md` ends at 992px). Keyed on **window width**, never device
idiom — a tablet in portrait is ~768-834 and correctly stays single column, the same tablet in
landscape does not, and split view is narrower again.

`MaximumWidthRequest` + `HorizontalOptions="Center"` do **not** compose in MAUI: `Center` sizes to
content and never reaches the cap. The cap is applied as side margins from
`AdaptiveLayout.ContentInset`.

### Dark surfaces
The players are dark in either OS theme. The now-playing bar is dark **on the player pages only**
(`OnDarkSurface="True"`) and themed elsewhere, matching how the web scopes
`.song-player-container .player-bar`. The navigation bar and logo follow the destination page via
`AppShell.ApplyChromeForCurrentPage`, recomputed on every navigation so dark chrome cannot strand
itself on an ordinary page.

---

## To check on iOS / iPadOS

Nothing here has run on Apple hardware. In rough priority:

1. **Lyrics timing.** The interpolating clock is fed by `MediaManagerPlaybackRuntime`'s ~1s
   heartbeat rather than Media3's polling loop. Watch a full verse: late or stuttering highlights
   mean the interpolator, a highlight vanishing between lines means something is consulting `Ends`.
2. **iPad, both orientations.** Portrait should look like the phone but capped and centred; landscape
   should put the stage and bio in a 360dp column beside the track list. Rotate repeatedly — the
   artist page hands its single panel instance between the footer and the side column on each
   crossing, and attaching a view that still has a parent throws.
3. **The queue-order bug (1 above) on iOS.** The fix is in the Android runtime. The Apple runtime is
   a different `IPlatformPlaybackRuntime` and was never verified to have or not have the same fault —
   walk home → featured card → artist name and check the title matches the audio. Note 3 above found
   and fixed one Apple-runtime queue fault of this family; it does not clear this one.
4. **Nav bar chrome.** Verified on Android only; Shell chrome is platform-specific.
5. **The launch-crash gates.** `test_sims.sh --profile quick` covers iPad, which is the form factor
   review rejected before. Note it scores **startup crashes only** — it never navigates to a player,
   so it will not catch a layout fault.

---

## Known gaps

- **Only the two players and the artist page are tablet-adapted.** Home, library, settings and the
  subscription page still run edge to edge at 1280dp. Visible, not broken.
- The web blurs the cover art behind its hero under a scrim. Not reproduced — a full-width real-time
  blur is the main-thread work the ANR branch has been removing.
- The web's track list is one bordered panel; a `CollectionView` cannot put its items inside a shared
  border, so rows stay separate cards under the heading rule.
- Non-Android platforms still use the 90-minute `DefaultSleepSafeContinuityWindow` rather than
  Android's full-queue window. Pre-existing, gated on iOS local-cache trustworthiness.

## Tooling notes

- `maui-deploy-android-tablet` boots `Tablet_API_35` (Medium Tablet, 800x1280dp) and installs.
  `deploy-avd.ps1` resolves the serial from the AVD **name** — with two AVDs defined, assuming
  `emulator-5554` installs onto the wrong device.
- That script sets `EmbedAssembliesIntoApk`. Debug defaults to Fast Deployment, which keeps
  assemblies out of the APK and pushes them to `.__override__/<abi>`; an uninstall wipes that, an
  incremental build skips the re-push, and the app aborts in monodroid **before any managed code
  runs** — no stack trace, no app log, just a process that vanishes.
- Two concurrent Android builds against this project corrupt the merged-resource state and fail with
  `APT2260 ... m3_ref_palette_dynamic_* not found`. `dotnet clean` does not clear it; delete
  `obj/Debug/net10.0-android/{res,lp,resourcepaths.cache}`.
