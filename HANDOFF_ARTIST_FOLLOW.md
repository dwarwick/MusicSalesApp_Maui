# Handoff — Artist follow, MAUI side

Branch: `work/artist-follow-engagement`, in **both** repos.
Written 2026-09-05.

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

**Not started:** everything a listener can see. No follow button, no Following page, no Artist
Messages page, no in-app preference toggles, no deep-link routing on a notification tap.

---

## 1. A registered device receiving nothing is the expected state today

Before debugging anything client-side, check the two server gates. **Both default off.**

- `PushNotificationsEnabled` — the admin switch at `/admin/settings` → "Phone Notifications".
  While off, the dispatcher returns before it looks at anything.
- `ReceiveArtistReleasePush` / `ReceiveArtistMessagePush` on the listener's own account.

Neither consumes the notification — rows stay pending, so switching either on later delivers the
backlog. And **registration deliberately keeps working while both are off**, because registering is
how the round trip gets proven before delivery is switched on.

Also check the gitignored config is present: `Platforms/Android/google-services.Test.json` and
`google-services.Production.json`. Without them `FirebaseApp.InitializeApp` returns null,
`AndroidPushRegistrationService.IsSupported` reads that as "no push", and the app runs normally
with no notifications and no error. A fresh clone builds fine and simply has no push.

---

## 2. iOS push — what is missing

The APNs half is complete. `AppDelegate` binds both selectors and hands the raw token to
`ApplePushTokenBroker`; authorization and `RegisterForRemoteNotifications` are correct; the
entitlement is in `Platforms/iOS/Entitlements.plist`, wired via `CodesignEntitlements`.

1. **The Firebase iOS SDK is not referenced at all.** `Xamarin.Firebase.Messaging` sits inside the
   `== 'android'` ItemGroup in the csproj. This is the blocker: FCM on iOS is a *relay*, so the
   device gets an APNs token — which it does — but Firebase has to exchange that for an FCM
   registration token, and the FCM token is what the server stores.
2. **`ApplePushRegistrationService.IsSupported` is hard-coded `false`**, on purpose. Flipping it
   today would register raw APNs tokens that FCM rejects on every send, which look exactly like
   uninstalled devices from the dispatcher's side. Once the binding lands: set
   `Messaging.SharedInstance.ApnsToken` from the AppDelegate callback, and return
   `Messaging.SharedInstance.FcmToken` from `GetTokenAsync`.
3. **Neither `Platforms/iOS/GoogleService-Info.{Test,Production}.plist` exists.** The csproj
   already carries the `Exists()`-guarded `BundleResource` items; they need downloading from the
   two Firebase consoles.
4. **Console configuration** — "Push Notifications" on the App ID with the provisioning profile
   **regenerated afterwards**, and the APNs auth key (Key ID `9RTLMRH4GX`, Team ID `K7ZGP97YV6`)
   uploaded under Cloud Messaging in **both** Firebase projects. A missing key fails silently, on
   iOS only.

### `aps-environment` is never rewritten — decide before shipping iOS push

`Platforms/iOS/Entitlements.plist` carries a comment claiming the value "is rewritten per
configuration rather than being switched by hand". **No such rewrite exists** — the csproj,
targets and publish scripts were searched and nothing touches it. The file ships a literal
`development`.

Harmless today because iOS registration is off, and correct for Debug and TestFlight. It bites the
moment iOS push ships: an App Store build carrying `development` gets tokens APNs rejects as
`BadDeviceToken`, which reads as a server misconfiguration rather than a build one.

Either add the MSBuild rewrite keyed on configuration, or correct the comment to say it is manual.
Do not leave it claiming something the build does not do.

---

## 3. The follow client — nothing exists yet

`SongListItemDto` on the server carries **`PersonaId`** — the first *stable* artist identifier this
app has been given, since `ArtistName` is a display string resolved through a fallback chain and
changes when a creator renames a persona. A null `PersonaId` means the song has no artist entity,
so the client must offer no follow control rather than inventing one from the name.

Sketch, from the original plan and still accurate:

- `SongDto` gains `PersonaId` and an `[ObservableProperty] IsFollowingArtist` — treated like
  `UserLikeStatus`, i.e. **not** `[JsonIgnore]`, so it rides along in the offline catalogue
  snapshot.
- A new `IFollowService` as its own `IHttpClientFactory`-only service, deliberately **not** added
  to `IMusicService`, which would oblige a pass-through in `OfflineAwareMusicService` for every
  member.
- Follow control on `PersonaSectionView` and as a `SongCardView` bindable command, matching how
  `LikeSongCommand` is supplied. Reuse `RequireAuthenticatedUserAsync("follow artists")`.
- New `FollowingPage` and `ArtistMessagesPage` as `MenuItem`s beside My Playlists — three edits
  each: `NavigationRoutes.cs`, `Routing.RegisterRoute` in `AppShell.xaml.cs`, and the
  `AddTransient` pair in `MauiProgram.cs`.
- New service/ViewModel files go in `Services/` and `ViewModels/`, which the test project compiles
  by glob, so they must not touch MAUI platform APIs.

### Three server rules to mirror, not rediscover

- **Self-follow is refused.** `PUT api/mobile/follows/{personaId}` answers `CannotFollowSelf` as a
  400, like every other domain refusal. The control must be *absent* on your own songs rather than
  present and failing.
- **One artist owns many cards.** Following from one card has to move every other card for that
  persona on screen. The web does this with a shared followed-persona set on the parent, not a
  broadcast — the equivalent here is a notifier the card ViewModels subscribe to, and it has to
  exist before `IsFollowingArtist` is worth anything.
- **"Follow as" needs a server endpoint that does not exist.** The PUT accepts
  `followAsPersonaId`, but nothing exposes `GetFollowAsOptionsAsync` — it is service-only and the
  web reads it directly. Until that endpoint is added, send nothing and follow anonymously, which
  fails in the safe direction.

### Deep linking

The payload already carries `PushDataKeys.Kind` / `PersonaId` / `SongId` / `EntityId`, and
`StreamTunesFirebaseMessagingService` copies every data key onto the launch intent as an extra. The
routing has everything it needs; nothing consumes it yet.

---

## 4. Meanwhile

The account-level push preferences are settable on the web at `/manage-account` — but only once an
admin has switched `PushNotificationsEnabled` on, since the checkboxes are hidden until then.
