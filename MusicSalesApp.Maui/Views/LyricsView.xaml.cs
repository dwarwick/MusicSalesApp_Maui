#nullable enable
using System.Diagnostics;
using MusicSalesApp.Common.Contracts;
using MusicSalesApp.Maui.Resources.Styles;
using MusicSalesApp.Maui.Services;

namespace MusicSalesApp.Maui.Views;

/// <summary>
/// Scrolling lyrics with the word being sung highlighted.
/// </summary>
/// <remarks>
/// <para>
/// <b>The refresh rate is decoupled from the data rate, and that is the central decision here.</b>
/// Position is sampled about once a second on every platform and there is no faster source, so a
/// <see cref="LyricsClock"/> fills the gaps and this ticks against that instead. The tick is
/// cheap on purpose - it compares one integer - and only touches the UI when the active word
/// actually changes, which is a few times a second rather than ten. That keeps this inside the
/// app's existing performance rules rather than fighting them.
/// </para>
/// <para>
/// Highlighting is a colour change and nothing else. Anything that alters text metrics - weight,
/// size, spacing - re-measures the whole label, and doing that several times a second on a page
/// that is also playing audio is exactly the kind of main-thread work this repo has spent effort
/// removing.
/// </para>
/// </remarks>
public partial class LyricsView : ContentView
{
    /// <summary>
    /// How often the highlight is re-evaluated.
    /// </summary>
    /// <remarks>
    /// Not a frame rate. Words last a few hundred milliseconds, so this is fine-grained enough to
    /// look continuous while being an order of magnitude cheaper than animating. The underlying
    /// samples arrive once a second regardless; ticking faster than this would buy nothing.
    /// </remarks>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>How long a hand scroll suppresses auto-scroll for.</summary>
    /// <remarks>
    /// Someone reading ahead should not be yanked back on the next line. The same idea as the
    /// now-playing bar's drag guard, which stops the timer fighting the user's thumb.
    /// </remarks>
    private static readonly TimeSpan ManualScrollGrace = TimeSpan.FromSeconds(4);

    public static readonly BindableProperty DocumentProperty =
        BindableProperty.Create(
            nameof(Document), typeof(LyricsTimingsDocument), typeof(LyricsView), null,
            propertyChanged: OnDocumentChanged);

    public static readonly BindableProperty TextSizeProperty =
        BindableProperty.Create(
            nameof(TextSize), typeof(double), typeof(LyricsView), 15d,
            propertyChanged: OnDocumentChanged);

    private readonly Stopwatch _hostClock = Stopwatch.StartNew();
    private readonly LyricsClock _clock;

    private LyricsTimeline _timeline = LyricsTimeline.Empty;
    private Span?[] _spanForEntry = [];
    private Label?[] _labelForLine = [];

    private IPlaybackService? _playbackService;
    private IDispatcherTimer? _timer;
    private bool _isActive;
    private int _activeIndex = -1;
    private int _activeLine = -1;
    private DateTime _lastManualScrollUtc = DateTime.MinValue;
    private bool _isProgrammaticScroll;

    public LyricsView()
    {
        InitializeComponent();
        _clock = new LyricsClock(() => _hostClock.ElapsedMilliseconds);
        Scroller.Scrolled += OnScrolled;
    }

    /// <summary>The timings to display, or null when this song has none.</summary>
    public LyricsTimingsDocument? Document
    {
        get => (LyricsTimingsDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>Line size in device-independent units. Set from a token, not a literal.</summary>
    public double TextSize
    {
        get => (double)GetValue(TextSizeProperty);
        set => SetValue(TextSizeProperty, value);
    }

    /// <summary>Injected by tests, which have no platform application to resolve from.</summary>
    public IPlaybackService? PlaybackServiceOverride { get; set; }

    /// <summary>
    /// Hand the view the player whose clock it should follow.
    /// </summary>
    /// <remarks>
    /// Pages pass the service they were constructed with. A view created inside a DataTemplate -
    /// a song card - has no constructor to inject into, so it calls this with nothing and the
    /// service is resolved the way EqualizerPlayButton resolves its own.
    /// </remarks>
    public void Initialize(IPlaybackService? playbackService = null)
    {
        var resolved = playbackService
            ?? PlaybackServiceOverride
            ?? IPlatformApplication.Current?.Services.GetService(typeof(IPlaybackService)) as IPlaybackService;

        if (resolved is null || ReferenceEquals(resolved, _playbackService))
        {
            return;
        }

        if (_isActive && _playbackService is not null)
        {
            _playbackService.StateChanged -= OnPlaybackStateChanged;
        }

        _playbackService = resolved;

        if (_isActive)
        {
            _playbackService.StateChanged += OnPlaybackStateChanged;
        }
    }

    /// <summary>
    /// Start following playback. Pages call this from OnAppearing.
    /// </summary>
    /// <remarks>
    /// Gated rather than always-on for the same reason the now-playing bar is: a lyrics panel
    /// behind another page, or hidden behind artwork, must not be waking the main thread.
    /// </remarks>
    public void Activate()
    {
        if (_isActive)
        {
            return;
        }

        _isActive = true;

        // A card never called Initialize explicitly; resolve on the way in rather than
        // requiring every caller to remember.
        Initialize();

        if (_playbackService is not null)
        {
            _playbackService.StateChanged += OnPlaybackStateChanged;
            _clock.Reset(_playbackService.Position);

            if (_playbackService.IsPlaying)
            {
                _clock.Start();
            }
        }

        _timer ??= CreateTimer();
        _timer.Start();
        Tick();
    }

    /// <summary>Stop following playback. Pages call this from OnDisappearing.</summary>
    public void Deactivate()
    {
        if (!_isActive)
        {
            return;
        }

        _isActive = false;
        _timer?.Stop();
        _clock.Stop();

        if (_playbackService is not null)
        {
            _playbackService.StateChanged -= OnPlaybackStateChanged;
        }
    }

    private IDispatcherTimer CreateTimer()
    {
        var timer = Dispatcher.CreateTimer();
        timer.Interval = TickInterval;
        timer.Tick += (_, _) => Tick();
        return timer;
    }

    private static void OnDocumentChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is LyricsView view)
        {
            view.Rebuild();
        }
    }

    /// <summary>
    /// Draw the document: one label per line, one span per word.
    /// </summary>
    /// <remarks>
    /// Untimed lines - blank separators and section markers - are drawn but never highlighted and
    /// never scrolled to. They are part of how the artist laid the song out, so removing them
    /// would not be showing the lyrics that were written; they simply have no time.
    /// </remarks>
    private void Rebuild()
    {
        LineStack.Children.Clear();
        _activeIndex = -1;
        _activeLine = -1;

        var document = Document;
        _timeline = LyricsTimeline.Build(document);

        if (document?.Lines is not { Count: > 0 } || _timeline.Count == 0)
        {
            EmptyLabel.IsVisible = true;
            _spanForEntry = [];
            _labelForLine = [];
            return;
        }

        EmptyLabel.IsVisible = false;
        _labelForLine = new Label?[document.Lines.Count];

        // An entry's span is found by index rather than by searching, because this lookup happens
        // on every highlight change.
        _spanForEntry = new Span?[_timeline.Count];
        var spanByLineWord = new Dictionary<(int Line, int Word), Span>();

        for (var lineIndex = 0; lineIndex < document.Lines.Count; lineIndex++)
        {
            var line = document.Lines[lineIndex];
            var label = BuildLine(line, lineIndex, spanByLineWord);
            _labelForLine[lineIndex] = label;
            LineStack.Children.Add(label);
        }

        for (var entry = 0; entry < _timeline.Count; entry++)
        {
            _spanForEntry[entry] = spanByLineWord.TryGetValue(
                (_timeline.LineOf(entry), _timeline.WordOf(entry)), out var span)
                ? span
                : null;
        }
    }

    private Label BuildLine(LyricsTimedLine line, int lineIndex, Dictionary<(int, int), Span> spanIndex)
    {
        var label = new Label
        {
            FontSize = TextSize,
            LineBreakMode = LineBreakMode.WordWrap,
            HorizontalTextAlignment = TextAlignment.Center
        };

        if (!line.IsTimed)
        {
            label.Text = line.Text;
            label.FontAttributes = FontAttributes.Italic;
            label.Opacity = 0.55;
            label.SetAppThemeColor(
                Label.TextColorProperty,
                AppColors.Get("Text3Light", "#5E6F85"),
                AppColors.Get("Text3Dark", "#8BA3C7"));
            return label;
        }

        var formatted = new FormattedString();
        var timedWords = line.Words.Where(w => w.IsTimed).ToList();

        if (timedWords.Count == 0)
        {
            // Placed as a line but not split into words. It highlights as one block, and word
            // index 0 is what the timeline emits for exactly this case.
            var span = MakeSpan(line.Text);
            spanIndex[(lineIndex, 0)] = span;
            formatted.Spans.Add(span);
        }
        else
        {
            for (var wordIndex = 0; wordIndex < line.Words.Count; wordIndex++)
            {
                var word = line.Words[wordIndex];
                var span = MakeSpan(word.Text);

                if (word.IsTimed)
                {
                    spanIndex[(lineIndex, wordIndex)] = span;
                }

                formatted.Spans.Add(span);

                if (wordIndex < line.Words.Count - 1)
                {
                    formatted.Spans.Add(MakeSpan(" "));
                }
            }
        }

        label.FormattedText = formatted;
        return label;
    }

    /// <summary>
    /// A word at rest.
    /// </summary>
    /// <remarks>
    /// The resting colour is an app-theme binding rather than a resolved value, so a word nobody
    /// has highlighted yet still follows a theme change. Highlighting overrides that binding and
    /// un-highlighting restores it.
    /// </remarks>
    private static Span MakeSpan(string? text) =>
        new Span { Text = text ?? string.Empty }.WithRestingColour();

    private void OnPlaybackStateChanged(string propertyName)
    {
        // Every other property is somebody else's business. Position is an anchor; IsPlaying
        // decides whether the clock should be running at all.
        if (propertyName == nameof(IPlaybackService.Position))
        {
            var service = _playbackService;
            if (service is not null)
            {
                _clock.Anchor(service.Position);
            }

            return;
        }

        if (propertyName == nameof(IPlaybackService.IsPlaying))
        {
            var service = _playbackService;
            if (service is null)
            {
                return;
            }

            if (service.IsPlaying)
            {
                _clock.Start();
            }
            else
            {
                _clock.Stop();
            }

            return;
        }

        if (propertyName == nameof(IPlaybackService.CurrentSong))
        {
            // A different song entirely - the old estimate is not stale, it is meaningless.
            _clock.Reset(TimeSpan.Zero);
        }
    }

    /// <summary>
    /// Re-evaluate the highlight. Cheap, and usually decides to do nothing.
    /// </summary>
    private void Tick()
    {
        if (!_isActive || _timeline.Count == 0)
        {
            return;
        }

        var index = _timeline.IndexAt(_clock.CurrentMs, _activeIndex);
        if (index == _activeIndex)
        {
            return;
        }

        SetActiveWord(index);
    }

    private void SetActiveWord(int index)
    {
        if (_activeIndex >= 0 && _activeIndex < _spanForEntry.Length)
        {
            _spanForEntry[_activeIndex]?.WithRestingColour();
        }

        _activeIndex = index;

        if (index < 0)
        {
            _activeLine = -1;
            return;
        }

        var span = index < _spanForEntry.Length ? _spanForEntry[index] : null;
        if (span is not null)
        {
            // A direct set, which replaces the resting theme binding until it is restored.
            span.TextColor = AppColors.Accent;
        }

        var line = _timeline.LineOf(index);
        if (line != _activeLine)
        {
            _activeLine = line;
            ScrollToLine(line);
        }
    }

    /// <summary>
    /// Bring the current line into view. Once per LINE, never per word.
    /// </summary>
    private void ScrollToLine(int lineIndex)
    {
        if (DateTime.UtcNow - _lastManualScrollUtc < ManualScrollGrace)
        {
            return;
        }

        if (lineIndex < 0 || lineIndex >= _labelForLine.Length)
        {
            return;
        }

        if (_labelForLine[lineIndex] is not { } label)
        {
            return;
        }

        _ = ScrollToLineAsync(label);
    }

    private async Task ScrollToLineAsync(Label label)
    {
        try
        {
            _isProgrammaticScroll = true;
            await Scroller.ScrollToAsync(label, ScrollToPosition.Center, animated: true);
        }
        catch
        {
            // A scroll that cannot complete - the view is being torn down, or the label is not
            // laid out yet - is not worth surfacing. The next line change will try again.
        }
        finally
        {
            _isProgrammaticScroll = false;
        }
    }

    private void OnScrolled(object? sender, ScrolledEventArgs e)
    {
        // Our own scrolls raise this too, so they must not be mistaken for the user's or the
        // panel would stop following the song the moment it started.
        if (!_isProgrammaticScroll)
        {
            _lastManualScrollUtc = DateTime.UtcNow;
        }
    }
}

/// <summary>Extension used so the resting colour is defined in exactly one place.</summary>
internal static class LyricsSpanExtensions
{
    public static Span WithRestingColour(this Span span)
    {
        span.SetAppThemeColor(
            Span.TextColorProperty,
            AppColors.Get("Text2Light", "#4A5B70"),
            AppColors.Get("Text2Dark", "#A8B6C8"));

        return span;
    }
}
