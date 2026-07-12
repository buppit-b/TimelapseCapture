using System;
using System.Windows;
using System.Windows.Media;

namespace FrameWrite.Wpf
{
    /// <summary>
    /// Swaps the app's colour palette at runtime. The palette brushes in App.xaml are mutable shared
    /// instances referenced via StaticResource, so changing their Color updates the whole UI live —
    /// no DynamicResource conversion or restart needed.
    /// </summary>
    public static class ThemeManager
    {
        public sealed class Theme
        {
            public string Name = "";
            public string Bg = "", Surface = "", SurfaceAlt = "", Stroke = "";
            public string TextPrimary = "", TextSecondary = "", Accent = "", Success = "", Danger = "";
        }

        public static readonly Theme[] Themes =
        {
            new() { Name = "Terminal", Bg = "#0E0E0E", Surface = "#161616", SurfaceAlt = "#1F1F1F", Stroke = "#2B2B2B", TextPrimary = "#E6EDE6", TextSecondary = "#7D8C7D", Accent = "#3FB950", Success = "#3FB950", Danger = "#E5534B" },
            new() { Name = "Ocean",    Bg = "#0C1116", Surface = "#121A22", SurfaceAlt = "#1A2531", Stroke = "#27323D", TextPrimary = "#E2ECF5", TextSecondary = "#7C8B9B", Accent = "#39AFE0", Success = "#36C28B", Danger = "#E5685B" },
            new() { Name = "Ember",    Bg = "#140F0C", Surface = "#1E1714", SurfaceAlt = "#2A211C", Stroke = "#382C25", TextPrimary = "#F2E9E2", TextSecondary = "#9A887C", Accent = "#E08A2B", Success = "#5CB85C", Danger = "#E5534B" },
            new() { Name = "Synth",    Bg = "#110C16", Surface = "#1A1322", SurfaceAlt = "#251A30", Stroke = "#33243F", TextPrimary = "#EEE6F5", TextSecondary = "#8E7DA0", Accent = "#C061F0", Success = "#48C9B0", Danger = "#F25C8A" },
            new() { Name = "Light",    Bg = "#F4F5F4", Surface = "#FFFFFF", SurfaceAlt = "#EAEDEA", Stroke = "#D4D8D4", TextPrimary = "#1A1F1A", TextSecondary = "#5C6B5C", Accent = "#2E9E40", Success = "#2E9E40", Danger = "#D1453B" },
        };

        public static void Apply(string? name)
        {
            var t = Array.Find(Themes, x => x.Name == name) ?? Themes[0];
            var res = Application.Current?.Resources;
            if (res == null) return;

            Set(res, "BgBrush", t.Bg);
            Set(res, "SurfaceBrush", t.Surface);
            Set(res, "SurfaceAltBrush", t.SurfaceAlt);
            Set(res, "StrokeBrush", t.Stroke);
            Set(res, "TextPrimaryBrush", t.TextPrimary);
            Set(res, "TextSecondaryBrush", t.TextSecondary);
            Set(res, "AccentBrush", t.Accent);
            Set(res, "SuccessBrush", t.Success);
            Set(res, "DangerBrush", t.Danger);
        }

        private static void Set(ResourceDictionary res, string key, string hex)
        {
            try
            {
                // Replace the resource (not mutate it) so DynamicResource references re-resolve live.
                res[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            }
            catch { /* leave the brush as-is on a bad value */ }
        }
    }
}
