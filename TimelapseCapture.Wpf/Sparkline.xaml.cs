using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TimelapseCapture.Wpf
{
    /// <summary>
    /// A tiny line-plus-fill sparkline with no charting dependency. Bind <see cref="Values"/> to a
    /// series (e.g. frames/min) and it draws a crisp auto-scaled trace, rebuilt against the real
    /// render size on data/size change. Y auto-scales to the series peak with a little headroom;
    /// an all-zero/flat series draws a flat line along the baseline (not a full-height artefact).
    /// </summary>
    public partial class Sparkline : UserControl
    {
        public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
            nameof(Values), typeof(double[]), typeof(Sparkline),
            new PropertyMetadata(null, (d, _) => ((Sparkline)d).Redraw()));

        public double[]? Values
        {
            get => (double[]?)GetValue(ValuesProperty);
            set => SetValue(ValuesProperty, value);
        }

        public Sparkline() => InitializeComponent();

        private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

        private void Redraw()
        {
            var empty = new PointCollection();
            line.Points = empty;
            fill.Points = empty;

            var vals = Values;
            double w = host.ActualWidth, h = host.ActualHeight;
            if (vals == null || vals.Length < 2 || w < 2 || h < 2) return;

            double max = Math.Max(1.0, vals.Max()) * 1.15;   // ≥1 avoids /0; headroom keeps the peak off the top edge
            double dx = w / (vals.Length - 1);
            const double pad = 1.5;                          // keep the stroke inside the clip bounds

            var pts = new PointCollection(vals.Length);
            for (int i = 0; i < vals.Length; i++)
            {
                double x = i * dx;
                double y = h - pad - (vals[i] / max) * (h - 2 * pad);
                pts.Add(new Point(x, y));
            }
            line.Points = pts;

            // Close the line down to the baseline for the translucent area fill.
            var area = new PointCollection(pts) { };
            area.Insert(0, new Point(0, h));
            area.Add(new Point(w, h));
            fill.Points = area;
        }
    }
}
