using System;
using System.Drawing;

namespace TimelapseCapture
{
    /// <summary>
    /// Draws the configurable text overlay onto a frame. ONE implementation shared by the capture
    /// engine (per-frame burn-in) and the Overlay dialog's live preview — so what the preview shows
    /// is exactly what lands in the frames, same GDI text rendering and all.
    /// </summary>
    public static class OverlayRenderer
    {
        /// <summary>Best-effort: an overlay problem must never break the capture loop.</summary>
        public static void Draw(Bitmap bmp, OverlayConfig cfg, long frameNumber = 0)
        {
            try
            {
                string text = ResolveTokens(cfg.Text, DateTime.Now, frameNumber);
                if (string.IsNullOrEmpty(text)) return;

                string family = string.IsNullOrWhiteSpace(cfg.FontFamily) ? "Consolas" : cfg.FontFamily;
                int px = cfg.FontSize > 0 ? cfg.FontSize : Math.Max(11, bmp.Height / 45);
                // The size setting is deliberately unlimited (power users), but rendering beyond the
                // frame is meaningless and giant GDI fonts get expensive — cap at the frame height.
                px = Math.Min(px, Math.Max(1, bmp.Height));
                using var font = new Font(family, px, FontStyle.Bold, GraphicsUnit.Pixel);
                using var g = Graphics.FromImage(bmp);
                var size = g.MeasureString(text, font);

                const float m = 8;
                float x, y;
                if (cfg.CustomX >= 0 && cfg.CustomY >= 0)
                {
                    // Free placement: normalized top-left, clamped so the text box stays fully on-frame.
                    x = (float)(cfg.CustomX * bmp.Width);
                    y = (float)(cfg.CustomY * bmp.Height);
                    x = Math.Max(m, Math.Min(x, bmp.Width - size.Width - m));
                    y = Math.Max(m, Math.Min(y, bmp.Height - size.Height - m));
                }
                else
                {
                    x = cfg.Position is 1 or 3 ? bmp.Width - size.Width - m : m;   // 1=TR, 3=BR → right
                    y = cfg.Position is 2 or 3 ? bmp.Height - size.Height - m : m; // 2=BL, 3=BR → bottom
                }

                using var bg = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
                g.FillRectangle(bg, x - 5, y - 2, size.Width + 10, size.Height + 4);
                g.DrawString(text, font, Brushes.White, x, y);
            }
            catch { /* best-effort by contract */ }
        }

        /// <summary>
        /// Replace {date}/{time}/{datetime}/{time12}, {frame}, and a custom {t:FORMAT} token.
        /// Pure (given a fixed clock + frame number) — kept public for the dialog preview and unit tests.
        /// </summary>
        public static string ResolveTokens(string template) => ResolveTokens(template, DateTime.Now, 0);

        internal static string ResolveTokens(string template, DateTime now, long frameNumber = 0)
        {
            string t = (template ?? "")
                .Replace("{datetime}", now.ToString("yyyy-MM-dd HH:mm:ss"))
                .Replace("{date}", now.ToString("yyyy-MM-dd"))
                .Replace("{time}", now.ToString("HH:mm:ss"))
                .Replace("{time12}", now.ToString("h:mm:ss tt"))
                .Replace("{frame}", frameNumber.ToString());
            return System.Text.RegularExpressions.Regex.Replace(t, @"\{t:([^}]+)\}", mm =>
            {
                try { return now.ToString(mm.Groups[1].Value); } catch { return mm.Value; }
            });
        }
    }
}
