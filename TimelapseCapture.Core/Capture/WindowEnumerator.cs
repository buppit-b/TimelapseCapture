using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace TimelapseCapture
{
    /// <summary>
    /// Enumerates top-level windows for the "track a window" capture source, and reads a window's live
    /// bounds. UI-agnostic (Core) — returns PHYSICAL-pixel bounds via GetWindowRect, matching the capture
    /// engine's BitBlt coordinate space. Ported from the legacy WinForms WindowSelector (minus the UI).
    /// </summary>
    public static class WindowEnumerator
    {
        public sealed record WindowInfo(IntPtr Handle, string Title, Rectangle Bounds, bool IsMinimized);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder s, int max);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        private const uint GW_OWNER = 4;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

        /// <summary>All visible, titled, top-level windows (curated like the legacy selector), sorted by title.</summary>
        public static IReadOnlyList<WindowInfo> Enumerate()
        {
            var windows = new List<WindowInfo>();
            IntPtr shell = GetShellWindow();

            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                int len = GetWindowTextLength(hWnd);
                if (len == 0) return true;

                var sb = new StringBuilder(len + 1);
                GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();
                if (string.IsNullOrWhiteSpace(title)) return true;

                if (hWnd == shell) return true;                          // the desktop
                if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return true; // owned/system popups

                if (!GetWindowRect(hWnd, out var r)) return true;
                var b = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                if (b.Width < 50 || b.Height < 50) return true;          // tray icons, slivers

                windows.Add(new WindowInfo(hWnd, title, b, IsIconic(hWnd)));
                return true;
            }, IntPtr.Zero);

            windows.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
            return windows;
        }

        /// <summary>
        /// Fresh bounds/state for a handle (pick-time and per-tick). <paramref name="alive"/> is false if the
        /// window no longer exists; <paramref name="minimized"/> true if iconic (its rect is off-screen junk).
        /// </summary>
        public static bool TryGetLiveBounds(IntPtr hWnd, out Rectangle bounds, out bool minimized, out bool alive)
        {
            bounds = Rectangle.Empty;
            minimized = false;
            alive = hWnd != IntPtr.Zero && IsWindow(hWnd);
            if (!alive) return false;

            minimized = IsIconic(hWnd);
            if (!GetWindowRect(hWnd, out var r)) return false;
            bounds = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            return true;
        }

        /// <summary>Force a window topmost (or release it) — used to keep a tracked window un-occluded.</summary>
        public static void SetTopmost(IntPtr hWnd, bool on)
        {
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return;
            SetWindowPos(hWnd, on ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }
}
