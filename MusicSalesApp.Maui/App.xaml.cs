using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui;

public partial class App : Application
{
	private readonly IAuthService _authService;
	private readonly IMusicService _musicService;

	public App(IAuthService authService, IMusicService musicService)
	{
		InitializeComponent();
		_authService = authService;
		_musicService = musicService;

		// Sync the Android system theme to MAUI at startup.
		// Application.Current is now set (we're in the constructor), so this is safe.
#if ANDROID
		var config = Android.App.Application.Context.Resources?.Configuration;
		if (config != null)
		{
			var nightMode = config.UiMode & Android.Content.Res.UiMode.NightMask;
			UserAppTheme = nightMode == Android.Content.Res.UiMode.NightYes
				? AppTheme.Dark
				: AppTheme.Light;
		}
#endif
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new AppShell(_authService));
	}

	protected override async void OnAppLinkRequestReceived(Uri uri)
	{
		base.OnAppLinkRequestReceived(uri);

		// Handle deep links like https://streamtunes.net/song/{title}
		if (uri.Scheme == "https"
		    && uri.AbsolutePath.StartsWith("/song/", StringComparison.OrdinalIgnoreCase)
		    && uri.AbsolutePath.Length > "/song/".Length)
		{
			var songTitle = Uri.UnescapeDataString(uri.AbsolutePath["/song/".Length..]);
			await Shell.Current.GoToAsync("song-player", new Dictionary<string, object>
			{
				["SongTitle"] = songTitle
			});
		}
		// Handle deep links like https://streamtunes.net/share/{id}
		// or custom scheme streamtunes://share/{id}
		else if (TryParseShareSongId(uri, out var songId))
		{
			var songs = await _musicService.GetSongsAsync();
			var song = songs.FirstOrDefault(s => s.Id == songId);
			if (song != null)
			{
				await Shell.Current.GoToAsync("song-player", new Dictionary<string, object>
				{
					["Song"] = song
				});
			}
		}
	}

	/// <summary>
	/// Tries to extract a song ID from a /share/{id} deep link URL.
	/// Supports both https://host/share/{id} and streamtunes://share/{id}.
	/// </summary>
	private static bool TryParseShareSongId(Uri uri, out int songId)
	{
		songId = 0;

		// Custom scheme: streamtunes://share/{id} → Host="share", AbsolutePath="/{id}"
		if (uri.Scheme.Equals("streamtunes", StringComparison.OrdinalIgnoreCase)
		    && uri.Host.Equals("share", StringComparison.OrdinalIgnoreCase)
		    && uri.AbsolutePath.Length > 1)
		{
			return int.TryParse(uri.AbsolutePath[1..], out songId);
		}

		// HTTPS scheme: https://host/share/{id}
		if (uri.Scheme == "https"
		    && uri.AbsolutePath.StartsWith("/share/", StringComparison.OrdinalIgnoreCase))
		{
			return int.TryParse(uri.AbsolutePath["/share/".Length..], out songId);
		}

		return false;
	}
}