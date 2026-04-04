# Copilot Instructions — MusicSalesApp MAUI

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

## Reference Documents
- `MAUI_REQUIREMENTS.md` — Full feature requirements for the Android app
- `MusicSalesApp/.github/copilot-instructions.md` — Web server conventions (for reference when adding API endpoints)
