using System;
using System.Drawing;

namespace TimelapseCapture
{
    /// <summary>
    /// Represents a readiness check with status indicator.
    /// </summary>
    public class ReadinessCheck
    {
        public string Name { get; set; }
        public ReadinessStatus Status { get; set; }
        public string Message { get; set; }
        public string Icon { get; set; }

        public ReadinessCheck(string name, ReadinessStatus status, string message, string icon = "")
        {
            Name = name;
            Status = status;
            Message = message;
            Icon = icon;
        }

        public Color GetColor()
        {
            return Status switch
            {
                ReadinessStatus.Ready => Color.FromArgb(0, 200, 100),      // Green
                ReadinessStatus.Warning => Color.FromArgb(255, 180, 0),    // Orange
                ReadinessStatus.Locked => Color.FromArgb(120, 120, 120),   // Gray
                ReadinessStatus.Error => Color.FromArgb(220, 50, 50),      // Red
                _ => Color.FromArgb(200, 200, 200)                         // Light gray
            };
        }

        public string GetStatusIcon()
        {
            return Status switch
            {
                ReadinessStatus.Ready => "✓",
                ReadinessStatus.Warning => "⚠",
                ReadinessStatus.Locked => "🔒",
                ReadinessStatus.Error => "✗",
                _ => "○"
            };
        }

        public string GetDisplayText()
        {
            return $"{GetStatusIcon()} {Icon} {Name}: {Message}";
        }
    }

    /// <summary>
    /// Status levels for readiness checks.
    /// </summary>
    public enum ReadinessStatus
    {
        Ready,      // Green checkmark - all good
        Warning,    // Yellow warning - needs attention
        Locked,     // Gray lock - prerequisite not met
        Error       // Red X - something wrong
    }
}
