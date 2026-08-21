using Microsoft.Maui.Controls.Shapes;
using MusicSalesApp.Maui.ViewModels;
using MusicSalesApp.Maui.Resources.Styles;

namespace MusicSalesApp.Maui.Services;

public class TipAmountPickerPage : ContentPage
{
    private readonly TaskCompletionSource<decimal?> _resultSource = new();
    private readonly TipAmountPickerViewModel _viewModel;

    public TipAmountPickerPage(string songTitle)
    {
        _viewModel = new TipAmountPickerViewModel(songTitle);

        BindingContext = _viewModel;
        BackgroundColor = Colors.Transparent;
        Shell.SetNavBarIsVisible(this, false);
        Content = BuildContent();
    }

    public Task<decimal?> WaitForResultAsync() => _resultSource.Task;

    protected override void OnDisappearing()
    {
        _resultSource.TrySetResult(null);
        base.OnDisappearing();
    }

    private View BuildContent()
    {
        var backdrop = new Grid
        {
            BackgroundColor = AppColors.Get("Scrim", "#CC000000")
        };
        backdrop.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await DismissAsync(null))
        });

        var container = new Grid();
        container.Children.Add(backdrop);
        container.Children.Add(BuildScrollHost());
        return container;
    }

    private View BuildScrollHost()
    {
        var scroll = new ScrollView
        {
            Padding = new Thickness(24),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Content = BuildDialog()
        };

        return scroll;
    }

    private View BuildDialog()
    {
        var dialog = new Border
        {
            MaximumWidthRequest = 380,
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = 28 },
            Padding = 24,
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(Colors.Black.WithAlpha(0.38f)),
                Offset = new Point(0, 10),
                Radius = 18,
                Opacity = 0.35f,
            },
            Content = new VerticalStackLayout
            {
                Spacing = 18,
                Children =
                {
                    BuildHeader(),
                    BuildSongCard(),
                    BuildPresets(),
                    BuildCustomAmountArea(),
                    BuildFooter(),
                    BuildErrorCard()
                }
            }
        };
        dialog.SetAppThemeColor(Border.BackgroundColorProperty, AppColors.Get("SurfaceLight", "#FFFFFF"), AppColors.Get("SurfaceDark", "#2A323D"));

        return dialog;
    }

    private View BuildHeader()
    {
        var title = new Label
        {
            FontSize = 22,
            FontAttributes = FontAttributes.Bold
        };
        title.SetBinding(Label.TextProperty, nameof(TipAmountPickerViewModel.Title));
        title.SetAppThemeColor(Label.TextColorProperty, Colors.Black, Colors.White);

        var tagline = new Label
        {
            Text = "Support the artist directly",
            FontSize = 13,
            TextColor = AppColors.Text2
        };

        var heart = new Label
        {
            Text = "❤",
            FontSize = 22,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            WidthRequest = 22,
            HeightRequest = 22,
            TextColor = GetAccentTextColor()
        };

        var heartShell = new Border
        {
            BackgroundColor = GetAccentSurfaceColor(),
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = 10,
            Content = heart
        };

        var titleStack = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { title, tagline }
        };

        var closeButton = new Button
        {
            Text = "✕",
            FontSize = 14,
            BackgroundColor = Colors.Transparent,
            TextColor = AppColors.Text2,
            Padding = 6,
            WidthRequest = 36,
            HeightRequest = 36,
            VerticalOptions = LayoutOptions.Start
        };
        closeButton.Clicked += async (_, _) => await DismissAsync(null);

        var headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
        };

        var leftStack = new HorizontalStackLayout
        {
            Spacing = 10,
            VerticalOptions = LayoutOptions.Center,
            Children = { heartShell, titleStack }
        };

        headerGrid.Children.Add(leftStack);
        Grid.SetColumn(closeButton, 1);
        headerGrid.Children.Add(closeButton);

        return headerGrid;
    }

    private View BuildSongCard()
    {
        var subtitle = new Label
        {
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };
        subtitle.SetBinding(Label.TextProperty, nameof(TipAmountPickerViewModel.Subtitle));
        subtitle.SetAppThemeColor(Label.TextColorProperty, Colors.Black, Colors.White);

        var detail = new Label
        {
            Text = "Choose a quick amount or enter your own.",
            FontSize = 13,
            TextColor = AppColors.Text2
        };

        var card = new Border
        {
            Stroke = Colors.Transparent,
            BackgroundColor = AppColors.SurfaceHover,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = new Thickness(16, 14),
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children = { subtitle, detail }
            }
        };

        return card;
    }

    private View BuildPresets()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10
        };

        var oneButton = CreatePresetButton("$1", async () => await DismissAsync(_viewModel.SelectPreset(1.00m)));
        var fiveButton = CreatePresetButton("$5", async () => await DismissAsync(_viewModel.SelectPreset(5.00m)));
        var tenButton = CreatePresetButton("$10", async () => await DismissAsync(_viewModel.SelectPreset(10.00m)));

        grid.Children.Add(oneButton);
        Grid.SetColumn(fiveButton, 1);
        grid.Children.Add(fiveButton);
        Grid.SetColumn(tenButton, 2);
        grid.Children.Add(tenButton);

        return grid;
    }

    private Button CreatePresetButton(string text, Func<Task> onClick)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = GetAccentSurfaceColor(),
            TextColor = GetAccentTextColor(),
            CornerRadius = 18,
            FontAttributes = FontAttributes.Bold,
            Padding = new Thickness(0, 12)
        };
        button.Clicked += async (_, _) => await onClick();
        return button;
    }

    private View BuildCustomAmountArea()
    {
        var customButton = new Button
        {
            Text = "Custom Amount",
            BackgroundColor = Colors.Transparent,
            BorderColor = AppColors.Line,
            BorderWidth = 1,
            TextColor = AppColors.Text,
            CornerRadius = 18
        };
        customButton.Clicked += (_, _) => _viewModel.RevealCustomInput();
        customButton.SetBinding(IsVisibleProperty, nameof(TipAmountPickerViewModel.ShowCustomAmountButton));

        var entry = new Entry
        {
            Keyboard = Keyboard.Numeric,
            Placeholder = "1.00 - 50.00",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing
        };
        entry.SetBinding(Entry.TextProperty, nameof(TipAmountPickerViewModel.CustomAmountText));
        entry.SetAppThemeColor(Entry.TextColorProperty, Colors.Black, Colors.White);

        var entryGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8
        };

        entryGrid.Children.Add(new Label
        {
            Text = "$",
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
            TextColor = GetAccentTextColor()
        });
        Grid.SetColumn(entry, 1);
        entryGrid.Children.Add(entry);

        var entryBorder = new Border
        {
            Stroke = AppColors.Line,
            BackgroundColor = AppColors.SurfaceHover,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = new Thickness(16, 4),
            Content = entryGrid
        };

        var sendButton = new Button
        {
            Text = "Send Tip",
            BackgroundColor = AppColors.AccentFill,
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 18
        };
        sendButton.Clicked += async (_, _) =>
        {
            if (_viewModel.TryGetCustomAmount(out var amount))
            {
                await DismissAsync(amount);
            }
        };

        var customStack = new VerticalStackLayout
        {
            Spacing = 10,
            IsVisible = false,
            Children = { entryBorder, sendButton }
        };
        customStack.SetBinding(IsVisibleProperty, nameof(TipAmountPickerViewModel.ShowCustomInput));

        return new VerticalStackLayout
        {
            Spacing = 12,
            Children = { customButton, customStack }
        };
    }

    private View BuildFooter()
    {
        return new Border
        {
            Stroke = Colors.Transparent,
            BackgroundColor = AppColors.SurfaceHover,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(14, 12),
            Content = new Label
            {
                Text = "Tips are processed via PayPal. Minimum $1, maximum $50.",
                FontSize = 12,
                HorizontalTextAlignment = TextAlignment.Center,
                TextColor = AppColors.Text2
            }
        };
    }

    private View BuildErrorCard()
    {
        var errorLabel = new Label
        {
            FontSize = 12,
            HorizontalTextAlignment = TextAlignment.Center,
            TextColor = AppColors.Danger
        };
        errorLabel.SetBinding(Label.TextProperty, nameof(TipAmountPickerViewModel.ErrorMessage));

        var errorCard = new Border
        {
            Stroke = Colors.Transparent,
            BackgroundColor = AppColors.DangerSoft,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(12, 10),
            IsVisible = false,
            Content = errorLabel
        };
        errorCard.SetBinding(IsVisibleProperty, nameof(TipAmountPickerViewModel.HasError));

        return errorCard;
    }

    private async Task DismissAsync(decimal? amount)
    {
        _resultSource.TrySetResult(amount);

        if (Navigation.ModalStack.LastOrDefault() == this)
        {
            await Navigation.PopModalAsync(false);
        }
    }

    private static Color GetThemeColor(string light, string dark)
    {
        return Application.Current?.RequestedTheme == AppTheme.Dark
            ? Color.FromArgb(dark)
            : Color.FromArgb(light);
    }

    private static Color GetAccentSurfaceColor()
    {
        var accent = AppColors.AccentFill;
        return Application.Current?.RequestedTheme == AppTheme.Dark
            ? accent.WithAlpha(0.28f)
            : accent.WithAlpha(0.12f);
    }

    private static Color GetAccentTextColor()
    {
        return Application.Current?.RequestedTheme == AppTheme.Dark
            ? AppColors.BlueBright
            : AppColors.AccentFill;
    }

    private static Color GetColorFromResources(string key, string fallback)
    {
        return Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : Color.FromArgb(fallback);
    }
}