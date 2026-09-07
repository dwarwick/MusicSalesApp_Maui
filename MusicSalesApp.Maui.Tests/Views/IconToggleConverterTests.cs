using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using MusicSalesApp.Maui.Resources.Styles;
using MusicSalesApp.Maui.Views;

namespace MusicSalesApp.Maui.Tests.Views;

/// <summary>
/// The like, dislike and follow icons are one control three times over. These pin the two things
/// that differ between them, because both are silent when wrong: which value counts as "on", and
/// which colour the off state takes.
/// </summary>
[TestFixture]
public class IconToggleConverterTests
{
    private static object? Convert(IValueConverter converter, object? value) =>
        converter.Convert(value, typeof(object), null, CultureInfo.InvariantCulture);

    private static Color FillFor(IValueConverter converter, object? value) =>
        ((SolidColorBrush)Convert(converter, value)!).Color;

    // ------------------------------------------------------------------ which value is "on"

    [Test]
    public void LikeAndFollow_AreOn_WhenTrue()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FillFor(new LikeFillConverter(), true), Is.EqualTo(AppColors.AccentFill));
            Assert.That(FillFor(new FollowBellFillConverter(), true), Is.EqualTo(AppColors.AccentFill));
        });
    }

    [Test]
    public void Dislike_IsOn_WhenFalse()
    {
        // Inverted on purpose: a thumbs-down is filled when the rating is false. This is the one
        // asymmetry in the family, and the reason IsOn is overridable rather than assumed.
        Assert.That(FillFor(new DislikeFillConverter(), false), Is.EqualTo(AppColors.Danger));
    }

    [Test]
    public void Dislike_IsOff_WhenTrue()
    {
        Assert.That(FillFor(new DislikeFillConverter(), true), Is.Not.EqualTo(AppColors.Danger));
    }

    [Test]
    public void NullRating_IsOff_ForEveryIcon()
    {
        // Unrated is the common case, and `is true` / `is false` both reject null - so neither
        // thumb is filled on a song the listener has not voted on.
        Assert.Multiple(() =>
        {
            Assert.That(FillFor(new LikeFillConverter(), null), Is.Not.EqualTo(AppColors.AccentFill));
            Assert.That(FillFor(new DislikeFillConverter(), null), Is.Not.EqualTo(AppColors.Danger));
            Assert.That(FillFor(new FollowBellFillConverter(), null), Is.Not.EqualTo(AppColors.AccentFill));
        });
    }

    // ------------------------------------------------------------------ the player exception

    [Test]
    public void PlayerBell_DoesNotConsultTheTheme_WhenOff()
    {
        // The player is dark in both app themes, so an off bell there is always the muted ink. The
        // page converter asks the theme and would paint it black onto a near-black player.
        Assert.That(FillFor(new FollowBellPlayerFillConverter(), false), Is.EqualTo(AppColors.Text3));
    }

    [Test]
    public void PlayerBell_UsesTheAccent_WhenFollowing()
    {
        Assert.That(FillFor(new FollowBellPlayerFillConverter(), true), Is.EqualTo(AppColors.AccentFill));
    }

    // ------------------------------------------------------------------ glyphs

    [Test]
    public void EachGlyph_ParsesToAGeometry_AndDiffersByState()
    {
        var converters = new IValueConverter[]
        {
            new LikeGlyphConverter(),
            new DislikeGlyphConverter(),
            new FollowBellGlyphConverter(),
        };

        Assert.Multiple(() =>
        {
            foreach (var converter in converters)
            {
                var on = Convert(converter, true);
                var off = Convert(converter, false);

                Assert.That(on, Is.InstanceOf<Geometry>(), converter.GetType().Name);
                Assert.That(off, Is.InstanceOf<Geometry>(), converter.GetType().Name);
                Assert.That(on, Is.Not.SameAs(off), converter.GetType().Name);
            }
        });
    }

    [Test]
    public void Glyphs_AreParsedOncePerState()
    {
        // The caching that keeps a scroll off the parser: these live in DataTemplate resources and
        // are rebound on every recycle, so re-parsing a ~180 character path each time cost the UI
        // thread real work during a flick.
        var converter = new FollowBellGlyphConverter();

        var first = Convert(converter, true);
        var second = Convert(converter, true);

        Assert.That(second, Is.SameAs(first));
    }
}
