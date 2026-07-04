namespace TimelapseCapture
{
    /// <summary>Configurable on-frame text overlay (timestamp / custom label).</summary>
    public sealed class OverlayConfig
    {
        public bool Enabled { get; set; }
        public string Text { get; set; } = "{datetime}";
        public int Position { get; set; } = 3;          // 0=top-left, 1=top-right, 2=bottom-left, 3=bottom-right
        public int FontSize { get; set; } = 0;          // pixels; 0 = auto (scale with frame height)
        public string FontFamily { get; set; } = "Consolas";

        // Free placement: normalized top-left of the text box (0..1 of frame W/H). Either < 0 means
        // "not set" → fall back to the corner Position. Set by dragging the overlay on the preview.
        public double CustomX { get; set; } = -1;
        public double CustomY { get; set; } = -1;
    }
}
