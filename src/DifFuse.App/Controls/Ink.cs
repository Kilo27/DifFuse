using System.Windows;
using System.Windows.Media;

namespace DifFuse.App.Controls;

/// <summary>Colours the custom-drawn controls use. Mirrors Themes/Colors.xaml.</summary>
internal static class Ink
{
    public static readonly Color Ground = Color.FromRgb(0x0A, 0x12, 0x30);
    public static readonly Color Panel = Color.FromRgb(0x0F, 0x1A, 0x40);
    public static readonly Color Track = Color.FromRgb(0x23, 0x30, 0x5E);
    public static readonly Color Detent = Color.FromRgb(0x5A, 0x67, 0x99);
    public static readonly Color Porcelain = Color.FromRgb(0xF3, 0xF1, 0xEA);
    public static readonly Color Muted = Color.FromRgb(0x8A, 0x95, 0xB8);

    public static readonly Brush GroundBrush = Freeze(new SolidColorBrush(Ground));
    public static readonly Brush PanelBrush = Freeze(new SolidColorBrush(Panel));
    public static readonly Brush TrackBrush = Freeze(new SolidColorBrush(Track));
    public static readonly Brush DetentBrush = Freeze(new SolidColorBrush(Detent));
    public static readonly Brush PorcelainBrush = Freeze(new SolidColorBrush(Porcelain));
    public static readonly Brush MutedBrush = Freeze(new SolidColorBrush(Muted));
    public static readonly Brush RingBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xD8, 0xF3, 0xF1, 0xEA)));

    public static readonly FontFamily Font = new("Bahnschrift");
    public static readonly Typeface Label = new(Font, FontStyles.Normal, FontWeights.Normal, FontStretches.SemiCondensed);
    public static readonly Typeface LabelBold = new(Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.SemiCondensed);

    public static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    public static Pen RoundPen(Brush brush, double thickness) => new(brush, thickness)
    {
        StartLineCap = PenLineCap.Round,
        EndLineCap = PenLineCap.Round,
    };
}
