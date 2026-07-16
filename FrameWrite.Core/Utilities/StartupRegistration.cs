using System;
using Microsoft.Win32;

namespace FrameWrite
{
    /// <summary>
    /// Registers/unregisters the app to launch at Windows sign-in via the per-user Run key
    /// (HKCU — no elevation needed). The value is re-written on every enable and self-healed at
    /// startup, so a moved/updated exe path never leaves a stale entry behind.
    /// </summary>
    public static class StartupRegistration
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "FrameWrite";

        /// <summary>The Run-key command line for an exe path — quoted, so paths with spaces survive.</summary>
        internal static string RunValue(string exePath) => $"\"{exePath}\"";

        /// <summary>Add or remove the sign-in launch entry. False = the registry write failed (logged).</summary>
        public static bool Apply(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                    ?? Registry.CurrentUser.CreateSubKey(RunKey);
                if (enabled)
                {
                    string? exe = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(exe)) return false;
                    key.SetValue(ValueName, RunValue(exe));
                    Logger.Log("Startup", $"Registered launch-at-sign-in: {exe}");
                }
                else
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                    Logger.Log("Startup", "Removed launch-at-sign-in registration.");
                }
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("Startup", $"Couldn't update the sign-in launch entry: {ex.Message}");
                return false;
            }
        }
    }
}
