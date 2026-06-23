using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TimelapseCapture
{
    /// <summary>
    /// Monitors user input activity (keyboard, mouse, stylus) for smart interval adjustment.
    /// Uses Windows low-level hooks to detect input globally across all applications.
    /// Thread-safe and properly disposable to prevent hook leaks.
    /// </summary>
    public class ActivityMonitor : IDisposable
    {
        #region Win32 API Declarations

        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_MOUSEWHEEL = 0x020A;

        private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        #endregion

        #region Fields

        private IntPtr _keyboardHookId = IntPtr.Zero;
        private IntPtr _mouseHookId = IntPtr.Zero;
        private readonly LowLevelProc _keyboardProc;
        private readonly LowLevelProc _mouseProc;

        private DateTime _lastActivityTime;
        private readonly object _activityLock = new object();
        private bool _isDisposed = false;

        // Debounce mouse movement to avoid constant activity from subtle movements
        private DateTime _lastMouseMoveTime = DateTime.MinValue;
        private const int MOUSE_MOVE_DEBOUNCE_MS = 1000; // Only count movement if > 1s since last

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets whether activity monitoring is enabled.
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// Gets or sets the idle threshold in seconds.
        /// If no activity for this duration, consider user idle.
        /// </summary>
        public int IdleThresholdSeconds { get; set; } = 30;

        /// <summary>
        /// Gets or sets whether to track mouse movement as activity.
        /// Disable if you want only clicks/keyboard to count as "active work".
        /// </summary>
        public bool TrackMouseMovement { get; set; } = true;

        /// <summary>
        /// Gets the last time any input activity was detected.
        /// Thread-safe.
        /// </summary>
        public DateTime LastActivityTime
        {
            get
            {
                lock (_activityLock)
                {
                    return _lastActivityTime;
                }
            }
        }

        #endregion

        #region Events

        /// <summary>
        /// Fired when user activity is detected after being idle.
        /// </summary>
        public event EventHandler? ActivityDetected;

        /// <summary>
        /// Fired when user becomes idle (no activity for IdleThresholdSeconds).
        /// </summary>
        public event EventHandler? IdleDetected;

        #endregion

        #region Constructor & Initialization

        /// <summary>
        /// Creates a new activity monitor.
        /// Call Start() to begin monitoring.
        /// </summary>
        public ActivityMonitor()
        {
            // Store delegates to prevent garbage collection
            _keyboardProc = KeyboardHookCallback;
            _mouseProc = MouseHookCallback;

            // Initialize to current time to avoid false idle at startup
            lock (_activityLock)
            {
                _lastActivityTime = DateTime.UtcNow;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Start monitoring user input activity.
        /// Installs global Windows hooks for keyboard and mouse.
        /// </summary>
        public void Start()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(ActivityMonitor));

            if (_keyboardHookId != IntPtr.Zero || _mouseHookId != IntPtr.Zero)
            {
                Debug.WriteLine("ActivityMonitor: Already started");
                return;
            }

            try
            {
                using (Process curProcess = Process.GetCurrentProcess())
                using (ProcessModule? curModule = curProcess.MainModule)
                {
                    if (curModule != null)
                    {
                        IntPtr moduleHandle = GetModuleHandle(curModule.ModuleName);

                        // Install keyboard hook
                        _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);
                        if (_keyboardHookId == IntPtr.Zero)
                        {
                            int error = Marshal.GetLastWin32Error();
                            throw new System.ComponentModel.Win32Exception(error, "Failed to set keyboard hook");
                        }

                        // Install mouse hook
                        _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, moduleHandle, 0);
                        if (_mouseHookId == IntPtr.Zero)
                        {
                            int error = Marshal.GetLastWin32Error();
                            // Clean up keyboard hook if mouse hook fails
                            if (_keyboardHookId != IntPtr.Zero)
                            {
                                UnhookWindowsHookEx(_keyboardHookId);
                                _keyboardHookId = IntPtr.Zero;
                            }
                            throw new System.ComponentModel.Win32Exception(error, "Failed to set mouse hook");
                        }

                        Logger.Log("ActivityMonitor", "Started - hooks installed");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log("ActivityMonitor", $"Failed to start: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Stop monitoring user input activity.
        /// Uninstalls all Windows hooks.
        /// </summary>
        public void Stop()
        {
            if (_keyboardHookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHookId);
                _keyboardHookId = IntPtr.Zero;
            }

            if (_mouseHookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHookId);
                _mouseHookId = IntPtr.Zero;
            }

            Logger.Log("ActivityMonitor", "Stopped - hooks uninstalled");
        }

        /// <summary>
        /// Check if user is currently active (activity within idle threshold).
        /// Thread-safe.
        /// </summary>
        public bool IsCurrentlyActive()
        {
            if (!IsEnabled)
                return false; // If disabled, always consider inactive

            lock (_activityLock)
            {
                var elapsed = DateTime.UtcNow - _lastActivityTime;
                return elapsed.TotalSeconds < IdleThresholdSeconds;
            }
        }

        /// <summary>
        /// Get time elapsed since last activity.
        /// Thread-safe.
        /// </summary>
        public TimeSpan TimeSinceLastActivity()
        {
            lock (_activityLock)
            {
                return DateTime.UtcNow - _lastActivityTime;
            }
        }

        /// <summary>
        /// Get current activity status as a string for UI display.
        /// </summary>
        public string GetActivityStatusString()
        {
            if (!IsEnabled)
                return "⚪ Monitoring disabled";

            var timeSince = TimeSinceLastActivity();
            var seconds = (int)timeSince.TotalSeconds;

            if (seconds < 5)
                return $"🟢 Active (now)";
            else if (seconds < IdleThresholdSeconds)
                return $"🟡 Recent ({seconds}s ago)";
            else
                return $"🔴 Idle ({seconds}s ago)";
        }

        #endregion

        #region Hook Callbacks

        /// <summary>
        /// Keyboard hook callback - detects all keyboard input.
        /// Runs on Windows message thread.
        /// </summary>
        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && IsEnabled)
                {
                    // Only count keydown events as activity (not key up)
                    if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                    {
                        RecordActivity("Keyboard");
                    }
                }
            }
            catch (Exception ex)
            {
                // Never throw from hook callback - will crash the hook chain
                Debug.WriteLine($"Keyboard hook error: {ex.Message}");
            }

            return CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }

        /// <summary>
        /// Mouse hook callback - detects all mouse input.
        /// Runs on Windows message thread.
        /// </summary>
        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && IsEnabled)
                {
                    int msg = wParam.ToInt32();

                    // Always count clicks as activity
                    if (msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || 
                        msg == WM_MBUTTONDOWN || msg == WM_MOUSEWHEEL)
                    {
                        RecordActivity("Mouse Click/Wheel");
                    }
                    // Count mouse movement only if tracking enabled and debounced
                    else if (TrackMouseMovement && msg == WM_MOUSEMOVE)
                    {
                        var now = DateTime.UtcNow;
                        if ((now - _lastMouseMoveTime).TotalMilliseconds >= MOUSE_MOVE_DEBOUNCE_MS)
                        {
                            _lastMouseMoveTime = now;
                            RecordActivity("Mouse Move");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Never throw from hook callback
                Debug.WriteLine($"Mouse hook error: {ex.Message}");
            }

            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        /// <summary>
        /// Record activity timestamp and fire events if transitioning from idle to active.
        /// Thread-safe.
        /// </summary>
        private void RecordActivity(string source)
        {
            bool wasIdle;

            lock (_activityLock)
            {
                var now = DateTime.UtcNow;
                var elapsed = now - _lastActivityTime;
                wasIdle = elapsed.TotalSeconds >= IdleThresholdSeconds;

                _lastActivityTime = now;
            }

            // Fire event if transitioning from idle to active. Do this outside the lock.
            // NOTE: no disk I/O (Logger.Log) here — this runs inside the low-level keyboard/mouse
            // hook callback, which Windows silently removes if the callback blocks too long.
            if (wasIdle)
            {
                ActivityDetected?.Invoke(this, EventArgs.Empty);
            }
        }

        #endregion

        #region Periodic Idle Check (Optional Helper)

        private bool _idleNotified;

        /// <summary>
        /// Check if user has become idle and fire event if so.
        /// Call this periodically (e.g., from UI timer) to detect idle transitions.
        /// </summary>
        public void CheckForIdleTransition()
        {
            if (!IsEnabled)
                return;

            var timeSince = TimeSinceLastActivity();
            bool isIdle = timeSince.TotalSeconds >= IdleThresholdSeconds;

            // Fire once on the active->idle edge. Edge-tracking (rather than a fixed time window)
            // means the transition can't be missed when the external poll cadence jitters.
            if (isIdle && !_idleNotified)
            {
                _idleNotified = true;
                Logger.Log("ActivityMonitor", $"User became idle ({timeSince.TotalSeconds:F0}s since activity)");
                IdleDetected?.Invoke(this, EventArgs.Empty);
            }
            else if (!isIdle && _idleNotified)
            {
                _idleNotified = false;
            }
        }

        #endregion

        #region IDisposable Implementation

        /// <summary>
        /// Dispose of resources and unhook all Windows hooks.
        /// CRITICAL: Must be called to prevent hook leaks.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
                return;

            if (disposing)
            {
                // Managed cleanup (none needed)
            }

            // Unmanaged cleanup - CRITICAL
            Stop();

            _isDisposed = true;
            Logger.Log("ActivityMonitor", "Disposed");
        }

        /// <summary>
        /// Finalizer as safety net in case Dispose() not called.
        /// Ensures hooks are uninstalled even if disposal is forgotten.
        /// </summary>
        ~ActivityMonitor()
        {
            Dispose(false);
        }

        #endregion
    }
}
