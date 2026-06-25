using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TimelapseCapture.Wpf
{
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
