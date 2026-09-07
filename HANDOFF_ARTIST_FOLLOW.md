# Handoff — Artist follow, MAUI side

Branch: `work/artist-follow-engagement`, in **both** repos.
Written 2026-09-05, last updated 2026-09-06.

> This branch also carries an unrelated fix merged from `work/fix-home-auth-on-cold-start`: Home
> rendered signed-out over a restored session after a force-close and reopen, because the cold-start
> restore lands while Home's first load is still running and `OnAuthStateChanged` drops the notice
> while a load is in flight. `LoadAsync` now compares the auth identity across itself. The bug was
> on master, not from the follow work.

> **The full handoff lives in the sibling repo**, at
> `../MusicSalesApp/HANDOFF_ARTIST_FOLLOW.md`. Read that first — it covers the server, the web UI,
> what has to be tested next and in what order, and the traps that already cost time. This file
> covers only what is specific to this repo.

Both repos must be **siblings on disk** (`<parent>/MusicSalesApp` and
`<parent>/MusicSalesApp_Maui`) — the csproj references `..\..\MusicSalesApp\MusicSalesApp.Common`
by relative path, and nothing builds otherwise.

---

## State of this repo

**Done:** push *registration* on Android — `AndroidPushRegistrationService`,
`StreamTunesFirebaseMessagingService`, the platform-neutral coordinator in `Services/`, and
`PushApiService` talking to the server. Covered by `PushNotificationCoordinatorTests`.

**Also done:** push preferences in the app (all of them on `ConfigPage`, one place), and tap
routing — `PushNotificationRouter` opens the song for a Release notification and the playlist for a
Digest, from both a cold launch and a backgrounded tap.

**Not started:** the follow *feature* a listener can see. No follow button, no Following page, no
Artist Messages page.

---

## 1. A registered device receiving nothing is the expected state today

Before debugging anything client-side, check the two server gates. **Both default off.**

- `PushNotificationsEnabled` — the admin switch at `/admin/settings` → "Phone Notifications".
  While off, the dispatcher returns before it looks at anything.
- `ReceiveArtistReleasePush` / `ReceiveArtistMessagePush` on the listener's own account.

Neither consumes the notification — rows stay pending, so switching either on later delivers the
backlog. And **registration deliberately keeps working while both are off**, because registering is
how the round trip gets proven before delivery is switched on.

Also check the gitignored config is present: `Platforms/Android/google-services.{Test,Production}.json`
and `Platforms/iOS/GoogleService-Info.{Test,Production}.plist`. Without the Android pair
`FirebaseApp.InitializeApp` returns null, `AndroidPushRegistrationService.IsSupported` reads that as
"no push", and the app runs normally with no notifications and no error. A fresh clone builds fine
and simply has no push.

> None of the four was covered by `.gitignore` until 2026-09-06 — they sat untracked but unignored,
> one `git add -A` from a public repo, project ids and API keys included. Rules are in place now.
> Check before staging if you are working in an older clone.

---

## 2. iOS push — what is missing

The APNs half is complete. `AppDelegate` binds both selectors and hands the raw token to
`ApplePushTokenBroker`; authorization and `RegisterForRemoteNotifications` are correct; the
entitlement is in `Platforms/iOS/Entitlements.plist`, wired via `CodesignEntitlements`.

**All four items are done, and push has been received on a device.** Kept here only as the record of
what iOS push needs, because every piece is invisible until one of them is missing:

1. ~~The Firebase iOS SDK is not referenced.~~ `AdamE.Firebase.iOS.CloudMessaging` 12.10.0.
2. ~~`IsSupported` is hard-coded `false`.~~ Now `true`; `Firebase.Core.App.Configure()` runs once
   behind a guard, the APNs token is handed to Firebase, and `GetTokenAsync` returns the FCM token.
3. ~~The iOS plists do not exist.~~ Both in place. Gitignored, so still restored per machine.
4. ~~Console configuration.~~ "Push Notifications" on the App ID with the profile regenerated
   afterwards, and the APNs auth key (Key ID `9RTLMRH4GX`, Team ID `K7ZGP97YV6`) uploaded under
   Cloud Messaging in **both** Firebase projects. A missing key fails silently, on iOS only.

### `aps-environment` is not in Entitlements.plist, and that is deliberate

It has to differ per configuration, so it is a `CustomEntitlements` item in the csproj —
`production` for Release, `development` otherwise — merged into the compiled entitlements by the
SDK's own `_CompileEntitlements`. Do not "fix" this by adding the key back to the plist; two
sources for one entitlement is how they drift.

**Release covers both TestFlight and the App Store.** TestFlight is not sandbox, and both are signed
with the same App Store profile. A Release build carrying `development` gets tokens APNs rejects as
`BadDeviceToken`, which reads as a server misconfiguration rather than a build one.

---

## 3. The follow client — the bell is built; the pages are not

`SongListItemDto` on the server carries **`PersonaId`** — the first *stable* artist identifier this
app has been given, since `ArtistName` is a display string resolved through a fallback chain and
changes when a creator renames a persona. A null `PersonaId` means the song has no artist entity,
so the client offers no follow control rather than inventing one from the name.

**Built**, and the shape it actually took:

- `SongDto` carries `PersonaId`, `[ObservableProperty] IsFollowingArtist` and `IsOwnArtist`, with
  `CanFollowArtist` as the single expression every bell binds to and every follow command guards
  on. Neither is `[JsonIgnore]`, so both ride along in the offline catalogue snapshot — which is
  why `OfflineSongCatalogStore.ClearUserStateAsync` strips them on sign-out alongside the votes.
- `IFollowService` is its own `IHttpClientFactory`-only service, as planned. Note
  `GetFollowedPersonaIdsAsync` returns `HashSet<int>?`: **null means the server could not be asked**
  and is not the same as an empty set. Collapsing them let a brief outage erase the cached follow
  set and visibly unfollow a user's whole library.
- `ArtistFollowStateCoordinator` is the singleton every surface goes through — it resolves a page
  in one round trip, stamps ownership, toggles optimistically and re-raises `FollowStateChanged`
  **on the UI thread**. Subscribers must still detach in `Cleanup()`: it is a singleton and the
  ViewModels are transient.
- `OwnMusicPolicy` decides "is this the user's own music" for the bell, the tip button, the preview
  limiter and stream counting. One rule; do not add a fifth copy.
- The bell is on `SongCardView` and `PersonaSectionView`, through `IconGlyphConverter` /
  `IconFillConverter`, which the like and dislike icons share.

**Still to build:** `FollowingPage` and `ArtistMessagesPage` as `MenuItem`s beside My Playlists —
three edits each: `NavigationRoutes.cs`, `Routing.RegisterRoute` in `AppShell.xaml.cs`, and the
`AddTransient` pair in `MauiProgram.cs`. New service/ViewModel files go in `Services/` and
`ViewModels/`, which the test project compiles by glob, so they must not touch MAUI platform APIs.

### Three server rules to mirror, not rediscover

- **Self-follow is refused.** `PUT api/mobile/follows/{personaId}` answers `CannotFollowSelf` as a
  400, like every other domain refusal. The control is *absent* on your own songs rather than
  present and failing — `OwnMusicPolicy` decides that, and `ApplyKnownState` stamps it. Any new
  song surface must run its songs through the coordinator or the bell reappears on your own music.
- **One artist owns many cards.** Following from one card moves every other card for that persona
  on screen, through `ArtistFollowStateCoordinator.FollowStateChanged`. Re-stamp the *whole* backing
  collection, not the filtered one and not just the current track — both mistakes were made and
  both leave a stale bell the user can reach without a reload.
- **"Follow as" needs a server endpoint that does not exist.** The PUT accepts
  `followAsPersonaId`, but nothing exposes `GetFollowAsOptionsAsync` — it is service-only and the
  web reads it directly. Until that endpoint is added, send nothing and follow anonymously, which
  fails in the safe direction.

### Deep linking — built

`PushNotificationRouter` handles a tapped notification: `PushNotificationKinds.Release` opens the
song player for `PushDataKeys.SongId`, `Digest` opens the playlist player. It is wired from both
`MainActivity` (Android) and `AppDelegate` (iOS), so a backgrounded tap routes rather than just
raising the app, and it is covered by `PushNotificationRouterTests`.

What has no route yet is anything that would need a Following or Artist Messages page — the payload
carries `PersonaId` and `EntityId` ready for it.

---

## 4. Meanwhile

The account-level push preferences are settable on the web at `/manage-account` — but only once an
admin has switched `PushNotificationsEnabled` on, since the checkboxes are hidden until then.
