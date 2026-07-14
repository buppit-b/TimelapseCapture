using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace FrameWrite.Wpf
{
    /// <summary>
    /// Attached behavior: put local:ClampFlash.Enabled="True" on a numeric TextBox whose binding source
    /// clamps or rounds input. After the LostFocus commit it re-reads the source (so a coerced value is
    /// never displayed stale) and, when the kept value differs numerically from what was typed, flashes
    /// the field's border red — the app-wide "your entry was adjusted, see the tooltip" cue.
    /// Generic by design: it detects coercion by comparing typed vs kept, so it needs no per-field wiring
    /// and is inert on fields that accept the input unchanged.
    /// </summary>
    public static class ClampFlash
    {
        public static readonly DependencyProperty EnabledProperty =
            DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(ClampFlash),
                new PropertyMetadata(false, OnEnabledChanged));

        public static void SetEnabled(DependencyObject o, bool v) => o.SetValue(EnabledProperty, v);
        public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);

        private static void OnEnabledChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            if (o is not TextBox tb) return;
            tb.LostFocus -= OnLostFocus;
            if (e.NewValue is true) tb.LostFocus += OnLostFocus;
        }

        private static void OnLostFocus(object sender, RoutedEventArgs e)
        {
            var tb = (TextBox)sender;
            string typed = tb.Text;
            // Check AFTER the commit and any deferred source-side notifications have settled
            // (Background priority runs after those), then snap the display to the source's real value.
            tb.Dispatcher.BeginInvoke(new Action(() =>
            {
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
                if (decimal.TryParse(typed, out var was) && decimal.TryParse(tb.Text, out var kept) && was != kept)
                    Flash(tb);
            }), DispatcherPriority.Background);
        }

        private static void Flash(TextBox tb)
        {
            // Animate a LOCAL brush (never the shared themed resource), then clear the local value so the
            // style's DynamicResource border comes back and live theme switching keeps working.
            var from = (tb.BorderBrush as SolidColorBrush)?.Color ?? Color.FromRgb(0x2B, 0x2B, 0x2B);
            var brush = new SolidColorBrush(from);
            tb.BorderBrush = brush;
            var anim = new ColorAnimation(Color.FromRgb(0xE5, 0x53, 0x4B), TimeSpan.FromMilliseconds(220))
            {
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(2),
            };
            anim.Completed += (s, e) => tb.ClearValue(Control.BorderBrushProperty);
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }
    }

    /// <summary>
    /// Attached behavior: a Button with local:WindowActions.IsClose="True" closes its owning window when
    /// clicked. Lets the shared dialog-chrome template's caption close button work without per-dialog code.
    /// </summary>
    public static class WindowActions
    {
        public static readonly DependencyProperty IsCloseProperty =
            DependencyProperty.RegisterAttached("IsClose", typeof(bool), typeof(WindowActions),
                new PropertyMetadata(false, OnIsCloseChanged));

        public static void SetIsClose(DependencyObject o, bool v) => o.SetValue(IsCloseProperty, v);
        public static bool GetIsClose(DependencyObject o) => (bool)o.GetValue(IsCloseProperty);

        private static void OnIsCloseChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            if (o is not Button b) return;
            b.Click -= OnClick;
            if (e.NewValue is true) b.Click += OnClick;
        }

        private static void OnClick(object sender, RoutedEventArgs e)
            => Window.GetWindow((DependencyObject)sender)?.Close();
    }

    /// <summary>
    /// Attached behavior: restrict a TextBox to numeric input — digits only, optionally allowing a
    /// single decimal point. Blocks typing and pasting of anything else, so the field can't hold junk.
    /// Usage: local:NumericInput.AllowDecimal="True" (decimals) or "False" (integers).
    /// Every field with this behavior also steps on the mouse wheel (hover, no click needed):
    /// ±Step per notch (default 1), Shift = ×10 coarse, Ctrl = ×0.1 fine on decimal fields.
    /// </summary>
    public static class NumericInput
    {
        // Nullable on purpose: WPF skips the change callback when an explicitly-set value equals the
        // metadata default, so a plain bool defaulting to false silently never hooked the handlers on
        // AllowDecimal="False" (integer) fields. With null as the default, ANY explicit set fires it.
        public static readonly DependencyProperty AllowDecimalProperty =
            DependencyProperty.RegisterAttached("AllowDecimal", typeof(bool?), typeof(NumericInput),
                new PropertyMetadata(null, OnChanged));

        public static void SetAllowDecimal(DependencyObject o, bool? v) => o.SetValue(AllowDecimalProperty, v);
        public static bool GetAllowDecimal(DependencyObject o) => (o.GetValue(AllowDecimalProperty) as bool?) ?? false;

        public static readonly DependencyProperty StepProperty =
            DependencyProperty.RegisterAttached("Step", typeof(double), typeof(NumericInput),
                new PropertyMetadata(1.0));

        public static void SetStep(DependencyObject o, double v) => o.SetValue(StepProperty, v);
        public static double GetStep(DependencyObject o) => (double)o.GetValue(StepProperty);

        // Proportional (curved) wheel step: the step scales with the current magnitude, so a fast interval
        // (0.05s) nudges by 0.01 while a slow one (45s) nudges by 10 — no more coarse 1s jumps near the
        // fast end, and no 0.01 → 1.01 leap. Shift/Ctrl still coarsen/refine on top.
        public static readonly DependencyProperty ProportionalProperty =
            DependencyProperty.RegisterAttached("Proportional", typeof(bool), typeof(NumericInput),
                new PropertyMetadata(false));
        public static void SetProportional(DependencyObject o, bool v) => o.SetValue(ProportionalProperty, v);
        public static bool GetProportional(DependencyObject o) => (bool)o.GetValue(ProportionalProperty);

        private static decimal ProportionalStep(decimal v)
        {
            v = Math.Abs(v);
            if (v <= 0.1m) return 0.01m;   // sub-0.1s (video-rate) — fine 0.01 steps
            if (v <= 1m) return 0.1m;      // 0.1–1s
            if (v <= 10m) return 1m;       // 1–10s
            return 10m;                    // slow rates — coarse
        }

        private static void OnChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            if (o is not TextBox tb) return;
            tb.PreviewTextInput -= OnPreviewTextInput;
            tb.PreviewTextInput += OnPreviewTextInput;
            tb.PreviewMouseWheel -= OnWheel;
            tb.PreviewMouseWheel += OnWheel;
            tb.LostFocus -= OnLostFocusRestore;
            tb.LostFocus += OnLostFocusRestore;
            DataObject.RemovePastingHandler(tb, OnPaste);
            DataObject.AddPastingHandler(tb, OnPaste);
        }

        // Emptied/garbled input never reaches the source (the binding's conversion fails silently),
        // which left the box showing nothing while the real value lived on. Snap the display back on
        // blur so a numeric box can never lie about what's actually set.
        private static void OnLostFocusRestore(object sender, RoutedEventArgs e)
        {
            var tb = (TextBox)sender;
            if (!decimal.TryParse(tb.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        }

        private static void OnWheel(object sender, MouseWheelEventArgs e)
        {
            var tb = (TextBox)sender;
            if (tb.IsReadOnly || e.Delta == 0) return;

            // The display may hold a stale or half-typed value — resync from the source before stepping.
            if (!decimal.TryParse(tb.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var cur))
            {
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
                if (!decimal.TryParse(tb.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out cur))
                    cur = 0m;
            }

            bool allowDecimal = GetAllowDecimal(tb);
            decimal step;
            if (GetProportional(tb)) step = ProportionalStep(cur);
            else { try { step = (decimal)GetStep(tb); } catch (OverflowException) { step = 1m; } }
            if (step <= 0m) step = 1m;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) step *= 10m;
            else if (allowDecimal && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) step *= 0.1m;
            if (!allowDecimal) step = Math.Max(1m, decimal.Round(step));

            var next = cur + (e.Delta > 0 ? step : -step);
            if (next < 0m) next = 0m;
            tb.Text = next.ToString(allowDecimal ? "0.###" : "0", CultureInfo.InvariantCulture);

            // Commit like a blur would, then re-read so any source-side clamping shows immediately.
            var binding = tb.GetBindingExpression(TextBox.TextProperty);
            if (binding != null)
            {
                binding.UpdateSource();
                binding.UpdateTarget();
            }
            else
            {
                // Unbound boxes (the crop X/Y/W/H fields) commit via a code-behind LostFocus handler.
                tb.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, tb));
            }
            e.Handled = true;
        }

        private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
            => e.Handled = !IsValid((TextBox)sender, e.Text);

        private static void OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            if (sender is TextBox tb && e.DataObject.GetData(typeof(string)) is string s && !IsValid(tb, s))
                e.CancelCommand();
        }

        private static bool IsValid(TextBox tb, string input)
        {
            bool allowDecimal = GetAllowDecimal(tb);
            foreach (char c in input)
            {
                if (char.IsDigit(c)) continue;
                if (allowDecimal && c == '.' && !tb.Text.Contains('.')) continue;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Attached behavior: step a Slider with the mouse wheel on hover — ±Amount per notch
    /// (Shift = ×10), clamped to the slider's range. Usage: local:WheelStep.Amount="1".
    /// On the frame scrubbers this doubles as wheel-scrubbing through frames.
    /// </summary>
    public static class WheelStep
    {
        public static readonly DependencyProperty AmountProperty =
            DependencyProperty.RegisterAttached("Amount", typeof(double), typeof(WheelStep),
                new PropertyMetadata(0.0, OnAmountChanged));

        public static void SetAmount(DependencyObject o, double v) => o.SetValue(AmountProperty, v);
        public static double GetAmount(DependencyObject o) => (double)o.GetValue(AmountProperty);

        private static void OnAmountChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            if (o is not Slider s) return;
            s.PreviewMouseWheel -= OnWheel;
            if (e.NewValue is double d && d > 0) s.PreviewMouseWheel += OnWheel;
        }

        private static void OnWheel(object sender, MouseWheelEventArgs e)
        {
            var s = (Slider)sender;
            if (e.Delta == 0) return;
            double amount = GetAmount(s);
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) amount *= 10;
            double next = s.Value + (e.Delta > 0 ? amount : -amount);
            s.Value = Math.Max(s.Minimum, Math.Min(s.Maximum, next));
            e.Handled = true;
        }
    }
}
