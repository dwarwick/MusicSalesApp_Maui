# MusicSalesApp Android App Requirements

## Overview
A native Android app built with .NET MAUI that connects to the same backend as the StreamTunes web application (MusicSalesApp). Uses native Android controls (not Blazor Hybrid). Communicates with the web server exclusively via HTTP API calls.

## Shared Infrastructure
- **Same Azure Blob Storage containers** as the web app for music files and album art
- **Same SQL Server database** and user accounts (ASP.NET Identity)
- **Same API endpoints** on the web server (MusicSalesApp)
- Configuration: `ApiBaseUrl` in `appsettings.json` points to the web server

## Core Features

### 1. Authentication
- **Login** with existing accounts (username/email + password) via `POST /api/auth/login`
- **Register** new accounts via a registration API endpoint
- Users log into the same accounts used on the web app
- Session/token management for authenticated API calls
- **No creator signup** — users cannot become creators in the Android app
- **No passkey authentication** for initial release

### 2. Music Library & Playback
- Display all active songs as a flat list of song cards
- Each song card shows:
  - Album art image (from Azure Blob Storage via SAS URL)
  - Song title
  - Artist name
  - Genre
  - Stream count
- Tap to play any song
- Mini-player controls on the active card: play/pause, stop, progress bar, volume
- Full song playback for authenticated users with active subscription
- 60-second preview for non-subscribers (matches web app behavior)
- Stream count recording when a qualifying listen occurs

### 3. Playlists
- Create, rename, and delete playlists
- Add/remove songs to/from playlists
- Play all songs in a playlist
- Same subscription rules as web app:
  - With subscription: add any song
  - Without subscription: add only owned (purchased) songs

### 4. Facebook Sharing
- Share songs to Facebook from the app
- **Deep linking**: when a user clicks a shared StreamTunes link on their Android device, it opens the song in the Android app's song player (not the web browser)
- Requires Android App Links / Intent Filters configuration

## Excluded Features (Not in Android App)
- **No admin pages or admin menu** (AdminSongManagement, admin grids, etc.)
- **No creator pages** (creator onboarding, upload, payout, tax forms, persona management)
- **No creator signup** — the "Become a Creator" flow is web-only
- **No song upload** capability
- **No subscription purchase** (initially — may add later)
- **No individual song purchase** (cart/PayPal checkout)
- **No theme switching** (use system theme or fixed theme)
- **No passkey authentication**

## Technical Architecture

### Pattern: MVVM
- **ViewModels/** — `CommunityToolkit.Mvvm` ObservableObjects, RelayCommands, and DTOs
- **Views/** — XAML pages with native MAUI controls
- **Services/** — HTTP API client services using `IHttpClientFactory`
- **No `Models/` folder** — `Models` is reserved for database entities; this app has none. DTOs go in `ViewModels/`

### Testing (CRITICAL)
- **Test project:** `MusicSalesApp.Maui.Tests` (NUnit + Moq)
- **Always create NUnit tests** for all new services, ViewModels, and non-trivial helpers
- Mock external dependencies (HttpClient, platform services) in tests
- Run `dotnet test MusicSalesApp.Maui.Tests/` after changes to verify no regressions

### Key Dependencies
- `CommunityToolkit.Mvvm` — MVVM framework (already in project)
- `CommunityToolkit.Maui` — Extended MAUI controls (already in project)
- `CommunityToolkit.Maui.MediaElement` — Audio playback
- `Azure.Storage.Blobs` — NOT used directly; all blob access via web server API SAS URLs
- `Microsoft.Extensions.Http` — HttpClient factory (already in project)

### API Communication
The MAUI app communicates with the web server via HTTP. It does NOT have direct database or service access.

**Existing endpoints to consume:**
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/auth/login` | POST | User login |
| `/api/auth/logout` | POST | User logout |
| `/api/music/url/{fileName}` | GET | Get SAS URL for streaming a song |
| `/api/music/{fileName}` | GET | Stream song via server proxy (fallback) |
| `/api/music/stream/{songMetadataId}` | POST | Record a stream event |
| `/api/music/stream-count/{songMetadataId}` | GET | Get current stream count |
| `/api/subscription/status` | GET | Check subscription status |

**New endpoints needed (to be added to MusicSalesApp):**
| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/music/songs` | GET | Get all active songs with metadata, art URLs, stream URLs |
| `/api/auth/register` | POST | Register new user account |

### Configuration
- `ApiBaseUrl` — Base URL of the web server
- `Azure.ContainerName` — For reference only (not used directly by MAUI app)
- Environment-specific settings in `appsettings.Development.json` / `appsettings.Production.json`

## Development Notes
- Android emulator cannot reach `localhost` — use `10.0.2.2`, LAN IP, or a deployed test server URL in `ApiBaseUrl`
- The web server must be running and accessible for the MAUI app to function
- All music data comes from the web server's database via API calls
- SAS URLs have a time-limited lifetime (typically 24 hours); the app should handle expiration gracefully
