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
            public string TextPrimary = "", TextSecondary = "", Accent = "", Accent2 = "", Success = "", Danger = "";
        }

        // Accent2 is a deliberately CONTRASTING second hue per scheme (Spike, 2026-07-13): the UI read as
        // one flat colour because the accent painted both labels and interactive bits. Accent2 gives labels
        // (section headers) their own hue so the primary accent can mean "action / live". Complementary pairs:
        // green↔amber, blue↔amber, orange↔teal, pink↔teal, and a warm amber for Light.
        public static readonly Theme[] Themes =
        {
            new() { Name = "Terminal", Bg = "#0E0E0E", Surface = "#161616", SurfaceAlt = "#1F1F1F", Stroke = "#2B2B2B", TextPrimary = "#E6EDE6", TextSecondary = "#7D8C7D", Accent = "#3FB950", Accent2 = "#E3B341", Success = "#3FB950", Danger = "#E5534B" },
            new() { Name = "Ocean",    Bg = "#0C1116", Surface = "#121A22", SurfaceAlt = "#1A2531", Stroke = "#27323D", TextPrimary = "#E2ECF5", TextSecondary = "#7C8B9B", Accent = "#39AFE0", Accent2 = "#E0A44B", Success = "#36C28B", Danger = "#E5685B" },
            new() { Name = "Ember",    Bg = "#140F0C", Surface = "#1E1714", SurfaceAlt = "#2A211C", Stroke = "#382C25", TextPrimary = "#F2E9E2", TextSecondary = "#9A887C", Accent = "#E08A2B", Accent2 = "#4FC9C0", Success = "#5CB85C", Danger = "#E5534B" },
            new() { Name = "Synth",    Bg = "#110C16", Surface = "#1A1322", SurfaceAlt = "#251A30", Stroke = "#33243F", TextPrimary = "#EEE6F5", TextSecondary = "#8E7DA0", Accent = "#C061F0", Accent2 = "#3FCB7C", Success = "#48C9B0", Danger = "#F25C8A" },
            // Light is a MID grey theme (not blinding white) with contrast carried by BOLD BLACK, NOT a hue —
            // FULLY monochrome except red for danger (Spike, 2026-07-13): accent + labels + Success all near-black.
            new() { Name = "Light",    Bg = "#D3D5D3", Surface = "#E3E5E3", SurfaceAlt = "#C7CAC7", Stroke = "#AEB2AE", TextPrimary = "#1B1E1B", TextSecondary = "#565A56", Accent = "#111311", Accent2 = "#111311", Success = "#111311", Danger = "#D1453B" },
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
            Set(res, "Accent2Brush", t.Accent2);
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
