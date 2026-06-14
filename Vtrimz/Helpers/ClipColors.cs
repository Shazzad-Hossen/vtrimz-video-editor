using System.Windows.Media;
using Vtrimz.Models;

namespace Vtrimz.Helpers;

public static class ClipColors
{
    private static readonly Color[] Palette =
    [
        Color.FromRgb(61, 90, 254),
        Color.FromRgb(108, 92, 231),
        Color.FromRgb(0, 184, 148),
        Color.FromRgb(253, 121, 168),
        Color.FromRgb(253, 203, 110),
        Color.FromRgb(0, 206, 201),
        Color.FromRgb(225, 112, 85),
        Color.FromRgb(116, 185, 255),
        Color.FromRgb(162, 155, 254),
        Color.FromRgb(85, 239, 196)
    ];

    public static Brush GetBrush(int colorIndex)
    {
        var color = Palette[Math.Abs(colorIndex) % Palette.Length];
        return new SolidColorBrush(color);
    }

    public static int NextIndex(int current) => (current + 1) % Palette.Length;
}
