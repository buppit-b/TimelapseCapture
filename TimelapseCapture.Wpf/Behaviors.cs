using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TimelapseCapture.Wpf
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
    /// </summary>
    public static class NumericInput
    {
        public static readonly DependencyProperty AllowDecimalProperty =
            DependencyProperty.RegisterAttached("AllowDecimal", typeof(bool), typeof(NumericInput),
                new PropertyMetadata(false, OnChanged));

        public static void SetAllowDecimal(DependencyObject o, bool v) => o.SetValue(AllowDecimalProperty, v);
        public static bool GetAllowDecimal(DependencyObject o) => (bool)o.GetValue(AllowDecimalProperty);

        private static void OnChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
        {
            if (o is not TextBox tb) return;
            tb.PreviewTextInput -= OnPreviewTextInput;
            tb.PreviewTextInput += OnPreviewTextInput;
            DataObject.RemovePastingHandler(tb, OnPaste);
            DataObject.AddPastingHandler(tb, OnPaste);
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
}
