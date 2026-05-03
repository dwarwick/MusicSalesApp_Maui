using Microsoft.Maui.Controls.Shapes;

namespace MusicSalesApp.Maui.Services;

public sealed class PermissionExplainerPage : ContentPage
{
    private readonly TaskCompletionSource<PermissionExplainerResult> _resultSource = new();
    private readonly CheckBox? _doNotAskAgainCheckBox;

    public PermissionExplainerPage(PermissionExplainerRequest request)
    {
        BackgroundColor = Colors.Transparent;
        Shell.SetNavBarIsVisible(this, false);
        (_doNotAskAgainCheckBox, Content) = BuildContent(request);
    }

    public Task<PermissionExplainerResult> WaitForResultAsync() => _resultSource.Task;

    protected override void OnDisappearing()
    {
        _resultSource.TrySetResult(new PermissionExplainerResult(false, _doNotAskAgainCheckBox?.IsChecked == true));
        base.OnDisappearing();
    }

    private (CheckBox? CheckBox, View Content) BuildContent(PermissionExplainerRequest request)
    {
        var container = new Grid();
        var backdrop = new Grid
        {
            BackgroundColor = Color.FromArgb("#A6000000")
        };

        if (request.AllowBackdropDismiss)
        {
            backdrop.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () => await DismissAsync(false))
            });
        }

        container.Children.Add(backdrop);
        var dialog = BuildDialog(request);
        container.Children.Add(new ScrollView
        {
            Padding = new Thickness(24),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Content = dialog.Content
        });

        return (dialog.CheckBox, container);
    }

    private (CheckBox? CheckBox, View Content) BuildDialog(PermissionExplainerRequest request)
    {
        var overline = new Label
        {
            Text = request.Overline.ToUpperInvariant(),
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            CharacterSpacing = 1.2,
            TextColor = Color.FromArgb("#1DB954")
        };

        var title = new Label
        {
            Text = request.Title,
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.WordWrap
        };
        title.SetAppThemeColor(Label.TextColorProperty, Colors.Black, Colors.White);

        var message = new Label
        {
            Text = request.Message,
            FontSize = 15,
            LineHeight = 1.35,
            LineBreakMode = LineBreakMode.WordWrap
        };
        message.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#444444"), Color.FromArgb("#D0D0D0"));

        CheckBox? doNotAskAgainCheckBox = null;
        HorizontalStackLayout? doNotAskAgainRow = null;
        if (request.ShowDoNotAskAgainOption)
        {
            doNotAskAgainCheckBox = new CheckBox
            {
                IsChecked = false,
                Color = Color.FromArgb("#1DB954"),
                VerticalOptions = LayoutOptions.Center
            };

            var doNotAskAgainLabel = new Label
            {
                Text = request.DoNotAskAgainText,
                FontSize = 14,
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.WordWrap
            };
            doNotAskAgainLabel.SetAppThemeColor(Label.TextColorProperty, Color.FromArgb("#444444"), Color.FromArgb("#D0D0D0"));

            var toggleGesture = new TapGestureRecognizer();
            toggleGesture.Tapped += (_, _) => doNotAskAgainCheckBox.IsChecked = !doNotAskAgainCheckBox.IsChecked;
            doNotAskAgainLabel.GestureRecognizers.Add(toggleGesture);

            doNotAskAgainRow = new HorizontalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    doNotAskAgainCheckBox,
                    doNotAskAgainLabel
                }
            };
        }

        var buttonRow = new Grid
        {
            ColumnSpacing = 12,
            HorizontalOptions = LayoutOptions.Fill
        };

        if (!string.IsNullOrWhiteSpace(request.SecondaryButtonText))
        {
            buttonRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            buttonRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            var secondaryButton = new Button
            {
                Text = request.SecondaryButtonText,
                BackgroundColor = Colors.Transparent,
                BorderColor = GetThemeColor("#D4D4D4", "#4A4A4A"),
                BorderWidth = 1,
                TextColor = GetThemeColor("#4A4A4A", "#E0E0E0"),
                CornerRadius = 20,
                HorizontalOptions = LayoutOptions.Fill,
                Padding = new Thickness(16, 12)
            };
            secondaryButton.Clicked += async (_, _) => await DismissAsync(false);
            buttonRow.Children.Add(secondaryButton);
            Grid.SetColumn(secondaryButton, 0);
        }
        else
        {
            buttonRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        var primaryButton = new Button
        {
            Text = request.PrimaryButtonText,
            BackgroundColor = Color.FromArgb("#1DB954"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 20,
            HorizontalOptions = LayoutOptions.Fill,
            Padding = new Thickness(16, 12)
        };
        primaryButton.Clicked += async (_, _) => await DismissAsync(true);
        buttonRow.Children.Add(primaryButton);
        Grid.SetColumn(primaryButton, buttonRow.ColumnDefinitions.Count - 1);

        var contentStack = new VerticalStackLayout
        {
            Spacing = 18,
            Children =
            {
                overline,
                title,
                message
            }
        };

        if (doNotAskAgainRow != null)
        {
            contentStack.Children.Add(doNotAskAgainRow);
        }

        contentStack.Children.Add(buttonRow);

        var dialog = new Border
        {
            MaximumWidthRequest = 420,
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = 28 },
            Padding = new Thickness(24),
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(Color.FromArgb("#60000000")),
                Offset = new Point(0, 10),
                Radius = 18,
                Opacity = 0.35f,
            },
            Content = contentStack
        };
        dialog.SetAppThemeColor(Border.BackgroundColorProperty, Colors.White, Color.FromArgb("#202020"));

        return (doNotAskAgainCheckBox, dialog);
    }

    private async Task DismissAsync(bool accepted)
    {
        _resultSource.TrySetResult(new PermissionExplainerResult(accepted, _doNotAskAgainCheckBox?.IsChecked == true));

        if (Navigation.ModalStack.Contains(this))
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
}