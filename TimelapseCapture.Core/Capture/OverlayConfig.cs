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
    }
}
