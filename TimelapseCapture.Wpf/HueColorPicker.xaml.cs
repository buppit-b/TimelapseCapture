using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TimelapseCapture.Wpf
{
    /// <summary>
    /// A compact, dependency-free colour picker: a colour chip that opens a popup with the classic
    /// saturation/value square + hue strip. Two-way binds SelectedHex ("#RRGGBB"); picking applies
    /// LIVE while dragging, so a bound preview follows the cursor.
    /// </summary>
    public partial class HueColorPicker : UserControl
    {
        public static readonly DependencyProperty SelectedHexProperty = DependencyProperty.Register(
            nameof(SelectedHex), typeof(string), typeof(HueColorPicker),
            new FrameworkPropertyMetadata("#FFFFFF", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHexChanged));

        public string SelectedHex
        {
            get => (string)GetValue(SelectedHexProperty);
            set => SetValue(SelectedHexProperty, value);
        }

        private double _h;        // 0..360
        private double _s = 1;    // 0..1
        private double _v = 1;    // 0..1
        private bool _updatingFromInside;
        private bool _svDrag, _hueDrag;

        public HueColorPicker()
        {
            InitializeComponent();
            SyncFromHex(SelectedHex);
        }

        private static void OnHexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var p = (HueColorPicker)d;
            if (!p._updatingFromInside) p.SyncFromHex(e.NewValue as string);
            else p.UpdateChip();   // our own push — the H/S/V state is already right
        }

        private void SyncFromHex(string? hex)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hex)) return;
                var c = (Color)ColorConverter.ConvertFromString(hex.Trim().StartsWith('#') ? hex.Trim() : "#" + hex.Trim());
                (_h, _s, _v) = RgbToHsv(c.R, c.G, c.B);
                UpdateChip();
                UpdateVisuals();
            }
            catch { /* leave the last valid state */ }
        }

        private void OnChipClick(object sender, MouseButtonEventArgs e) => popup.IsOpen = !popup.IsOpen;

        private void OnPopupOpened(object? sender, EventArgs e) => Dispatcher.BeginInvoke(UpdateVisuals);

        // ---- saturation/value square ----
        private void OnSvDown(object sender, MouseButtonEventArgs e)
        {
            _svDrag = true;
            svSquare.CaptureMouse();
            ApplySv(e.GetPosition(svSquare));
        }

        private void OnSvMove(object sender, MouseEventArgs e)
        {
            if (_svDrag) ApplySv(e.GetPosition(svSquare));
        }

        private void OnSvUp(object sender, MouseButtonEventArgs e)
        {
            _svDrag = false;
            svSquare.ReleaseMouseCapture();
        }

        private void ApplySv(Point pt)
        {
            double w = svSquare.ActualWidth, h = svSquare.ActualHeight;
            if (w < 1 || h < 1) return;
            _s = Math.Clamp(pt.X / w, 0, 1);
            _v = Math.Clamp(1 - pt.Y / h, 0, 1);
            PushColor();
        }

        // ---- hue strip ----
        private void OnHueDown(object sender, MouseButtonEventArgs e)
        {
            _hueDrag = true;
            hueStrip.CaptureMouse();
            ApplyHue(e.GetPosition(hueStrip));
        }

        private void OnHueMove(object sender, MouseEventArgs e)
        {
            if (_hueDrag) ApplyHue(e.GetPosition(hueStrip));
        }

        private void OnHueUp(object sender, MouseButtonEventArgs e)
        {
            _hueDrag = false;
            hueStrip.ReleaseMouseCapture();
        }

        private void ApplyHue(Point pt)
        {
            double w = hueStrip.ActualWidth;
            if (w < 1) return;
            _h = Math.Clamp(pt.X / w, 0, 1) * 360.0;
            PushColor();
        }

        private void PushColor()
        {
            var (r, g, b) = HsvToRgb(_h, _s, _v);
            _updatingFromInside = true;
            SelectedHex = $"#{r:X2}{g:X2}{b:X2}";
            _updatingFromInside = false;
            UpdateVisuals();
        }

        private void UpdateChip()
        {
            var (r, g, b) = HsvToRgb(_h, _s, _v);
            chip.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        private void UpdateVisuals()
        {
            UpdateChip();
            var (hr, hg, hb) = HsvToRgb(_h, 1, 1);
            svHueLayer.Fill = new SolidColorBrush(Color.FromRgb(hr, hg, hb));
            if (svSquare.ActualWidth > 1)
            {
                Canvas.SetLeft(svThumb, _s * svSquare.ActualWidth - svThumb.Width / 2);
                Canvas.SetTop(svThumb, (1 - _v) * svSquare.ActualHeight - svThumb.Height / 2);
            }
            if (hueStrip.ActualWidth > 1)
                Canvas.SetLeft(hueThumb, _h / 360.0 * hueStrip.ActualWidth - hueThumb.Width / 2);
        }

        // ---- HSV ↔ RGB ----
        private static (double h, double s, double v) RgbToHsv(byte r, byte g, byte b)
        {
            double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
            double max = Math.Max(rd, Math.Max(gd, bd)), min = Math.Min(rd, Math.Min(gd, bd));
            double d = max - min;
            double h = 0;
            if (d > 0)
            {
                if (max == rd) h = 60 * (((gd - bd) / d) % 6);
                else if (max == gd) h = 60 * ((bd - rd) / d + 2);
                else h = 60 * ((rd - gd) / d + 4);
                if (h < 0) h += 360;
            }
            return (h, max == 0 ? 0 : d / max, max);
        }

        private static (byte r, byte g, byte b) HsvToRgb(double h, double s, double v)
        {
            double c = v * s;
            double x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
            double m = v - c;
            (double r, double g, double b) = ((int)(h / 60.0) % 6) switch
            {
                0 => (c, x, 0.0),
                1 => (x, c, 0.0),
                2 => (0.0, c, x),
                3 => (0.0, x, c),
                4 => (x, 0.0, c),
                _ => (c, 0.0, x),
            };
            return ((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }
    }
}
