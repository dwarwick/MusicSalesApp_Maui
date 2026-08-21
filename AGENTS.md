# Agent Instructions - MusicSalesApp MAUI

## Working Branches

- Before editing files, always check the current branch with `git branch --show-current`.
- If the current branch is `master`, create and switch to an appropriately named working branch before making changes.
- Use clear task-based branch names such as `work/featured-playback-queue-rotation`.
- Do not make code edits on `master` unless the user explicitly asks for that.

## Original Requirements

The goal is to create an Android app based on the MusicSalesApp web server solution. The Android app will:
1. Play music uploaded to the same Azure storage containers used by the web server
2. Log in and register with the same user accounts as the web server
3. Use native Android controls (not Blazor Hybrid)
4. NOT have admin pages, admin menu, or creator pages
5. Only allow users to log in, listen to full songs, and register for new accounts
6. Users cannot sign up to be a creator in the Android app
7. Users can share music to Facebook; clicking a shared post on Android opens the song in the app
8. Users can create and listen to playlists

## Testing Requirements (CRITICAL)

- **Always create NUnit tests** for all new logic added to the MAUI project
- Test project: `MusicSalesApp.Maui.Tests` (NUnit + Moq)
- Every new service, ViewModel, or non-trivial helper must have corresponding unit tests
- Run `dotnet test MusicSalesApp.Maui.Tests/` after changes to verify no regressions
- Mock external dependencies (HttpClient, platform services) in tests

## Project Conventions

### Architecture
- **MVVM pattern** using `CommunityToolkit.Mvvm`
- **ViewModels/** — ObservableObjects, RelayCommands, and DTOs
- **Services/** — HTTP API client services using `IHttpClientFactory`
- **Views/** — XAML pages with native MAUI controls
- **No `Models/` folder** — `Models` is reserved for database entities; this app has none. DTOs go in `ViewModels/`.

### API Communication
- All data comes from the web server via HTTP API calls
- Use `IHttpClientFactory` with the named client `"MusicSalesApi"` (configured in `MauiProgram.cs`)
- Never access the database directly — the MAUI app is a client

### No Albums
- Albums are legacy. Every song is standalone.
- Do not group songs by album or implement album-related logic.

### Code Style
- Use `[ObservableProperty]` and `[RelayCommand]` source generators from CommunityToolkit.Mvvm
- Keep code-behind files minimal — logic belongs in ViewModels
- Use dependency injection for all services
- **DRY (Don't Repeat Yourself):** Do not duplicate code. Extract shared logic into reusable helper methods or services
- **Small methods:** Keep methods focused on a single responsibility. Break large methods into smaller, well-named helpers
- Prefer extracting a helper over copy-pasting similar code blocks

## Styling and the design system

The app has a design system. Use it — a raw value in a view is a bug, not a shortcut.

**Where things live**

| File | Holds |
| --- | --- |
| `Resources/Styles/Colors.xaml` | Every colour. Brand, player, themed Light/Dark pairs, status |
| `Resources/Styles/Tokens.xaml` | Every non-colour value. Type, spacing, radii, sizes, elevation, motion |
| `Resources/Styles/Styles.xaml` | Implicit styles, and named styles for recurring components |
| `Resources/Styles/AppColors.cs` | The same colours, for code that draws without XAML |

**The rules**

- **No hex literal in a view.** Not in XAML, not in C#. If a colour is missing, add a token.
- **No bare number** for `FontSize`, `Padding`, `Margin`, `Spacing`, `CornerRadius` or a control
  size. Use the token holding that value; add one if the scale genuinely lacks a rung.
- **Reach for a named style before writing attributes.** `PillButtonPrimary`, `FilterPill`,
  `CardSurface`, `SectionHeading`, `SecondaryText` and the rest exist because those recipes were
  previously copy-pasted across five files.
- **Third-party brand colours are exempt and already named** — `FacebookBrand`,
  `GoogleButtonBorder`. They are prescribed by those platforms; do not re-theme them to our blues.

**The source of truth is the web app.** Values come from `../MusicSalesApp/MusicSalesApp/wwwroot/`:
`tokens.css` for brand, player and scale; the `:root` blocks of `light.css` and `dark.css` for the
themed pairs. When one of those changes, change these to match. The two clients drifted for a long
time precisely because nothing said this out loud — the mobile app was still shipping the Spotify
green the web app had retired, under a comment claiming the two matched.

**Four invariants. These are measurements, not preferences.**

1. **Never white text on `StBlue` `#0186FD`** — 3.6:1, fails AA. A filled control with a white
   label uses `StBlueDeep` `#0166D6`.
2. **A bright fill takes a dark foreground.** The `On*` family (`OnAccent`, `OnGenre`, `OnWarn`) is
   the only foreground allowed on top of a fill. Dark theme keeps the bright fill and flips the
   label near-black; it never dims the fill so that white works. Three separate AA failures in this
   app came from getting this backwards.
3. **The player is dark in both themes.** Bind player surfaces to the flat `Player*` colours, never
   to a themed pair. `StBlueBright` measures ~2.1:1 on white, so a light player would need a
   different accent and the signature moment of the design would change with a user preference.
4. **A card is raised in both themes, and a shadow is always dark.** `SurfaceDark` is deliberately
   lighter than `PageDark`. Never add a light-theme-only shadow, and never a white one.

**Two places the system does not reach on its own**

- `Platforms/Android/Resources/values/colors.xml` and `values-night/colors.xml` are Android's
  native chrome and cannot read `Colors.xaml`. Change them in the same commit.
- Theme-swapped image assets (`Resources/Images/*_black.svg` / `*_white.svg`, the logo PNGs, and
  `hero_animation.json`) have colour baked in. A palette change means re-cutting them.

**Checking your work**

Both of these should return nothing but token definitions and documented exceptions:

```bash
grep -rn '#[0-9A-Fa-f]\{3,8\}' --include=*.xaml Views/ AppShell.xaml
grep -rnE '(FontSize|CornerRadius|Spacing)="[0-9]+"' --include=*.xaml Views/ AppShell.xaml
```

Prefer `SetAppThemeColor` over reading a single colour at construction time. A colour resolved
once freezes whatever the theme was when the page was built, which is why some screens used to
pick up dark mode only after a relaunch.

## Reference Documents
- `MAUI_REQUIREMENTS.md` — Full feature requirements for the Android app
- `MusicSalesApp/AGENTS.md` - Web server conventions, and the design system this app ports from
