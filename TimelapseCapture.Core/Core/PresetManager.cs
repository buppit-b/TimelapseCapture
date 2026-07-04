using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TimelapseCapture
{
    /// <summary>
    /// Named configuration presets ("setups"). A preset is a <see cref="CaptureSettings"/> with the
    /// machine-identity, session-identity, UI-state, and safety-net fields stripped — so applying one
    /// swaps the capture/encode/look setup WITHOUT repointing your output folder, breaking the current
    /// session's region, yanking you out of Simple mode, or silently disabling low-disk auto-stop.
    ///
    /// Stored as one JSON file per preset under {AppPaths.DataDir}\presets — byte-compatible with an
    /// exported settings file (so the two features share a format), additive and crash-safe by reusing
    /// the same serializer + atomic-write pattern as SettingsManager. Distinct from Export/Import, which
    /// moves ALL settings (incl. folder + safety) between machines.
    /// </summary>
    public static class PresetManager
    {
        public static string PresetsDir { get; } = Path.Combine(AppPaths.DataDir, "presets");

        // The ONE source of truth for what a preset does NOT carry. Copies these fields from `src` to
        // `dst`; everything else is a "carried" field. Used two ways: apply (excluded ← live) and
        // save-strip (excluded ← defaults).
        private static void CopyExcluded(CaptureSettings src, CaptureSettings dst)
        {
            // Machine identity — applying a preset must never repoint capture at a foreign/dead path.
            dst.SaveFolder = src.SaveFolder;
            dst.FfmpegPath = src.FfmpegPath;
            // Session identity — the live region lives in session.json; the CaptureSettings.Region field
            // is legacy and must not travel (would threaten image2 frame uniformity).
            dst.Region = src.Region;
            // UI / first-run state — a preset must not flip Simple mode or re-trigger the wizard.
            dst.SimpleMode = src.SimpleMode;
            dst.FirstRunCompleted = src.FirstRunCompleted;
            dst.GuidedModeEnabled = src.GuidedModeEnabled;
            // Safety nets — default-on data-integrity behaviour must survive a preset swap.
            dst.AutoStopOnLowDisk = src.AutoStopOnLowDisk;
            dst.LowDiskStopMB = src.LowDiskStopMB;
            dst.MaxDurationEnabled = src.MaxDurationEnabled;
            dst.MaxDurationMinutes = src.MaxDurationMinutes;
            dst.StopAtStorageEnabled = src.StopAtStorageEnabled;
            dst.StopAtStorageMB = src.StopAtStorageMB;
            dst.StopAtTarget = src.StopAtTarget;
            dst.NotifyOnFinish = src.NotifyOnFinish;
            // Global hotkey — a system-registration side effect, not a capture-look preference.
            dst.HotkeysEnabled = src.HotkeysEnabled;
            dst.HotkeyModifiers = src.HotkeyModifiers;
            dst.HotkeyVk = src.HotkeyVk;
        }

        private static CaptureSettings Clone(CaptureSettings s) =>
            JsonSerializer.Deserialize<CaptureSettings>(JsonSerializer.Serialize(s)) ?? new CaptureSettings();

        /// <summary>Clone with the excluded identity/safety fields reset to defaults (what gets written).</summary>
        internal static CaptureSettings StripIdentity(CaptureSettings live)
        {
            var c = Clone(live);
            CopyExcluded(new CaptureSettings(), c);   // excluded ← fresh defaults
            return c;
        }

        /// <summary>
        /// Merge a loaded preset over live settings: carried fields come from the preset, excluded
        /// identity/safety fields are preserved from <paramref name="live"/>. Pure (does not save).
        /// </summary>
        public static CaptureSettings ApplyOnto(CaptureSettings preset, CaptureSettings live)
        {
            var c = Clone(preset);
            CopyExcluded(live, c);   // excluded ← the user's current values
            return c;
        }

        /// <summary>Preset display names (filename without extension), sorted; unparseable files skipped.</summary>
        public static IReadOnlyList<string> List()
        {
            try
            {
                if (!Directory.Exists(PresetsDir)) return Array.Empty<string>();
                return Directory.GetFiles(PresetsDir, "*.json")
                    .Where(f => Load(Path.GetFileNameWithoutExtension(f)) != null)
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToArray()!;
            }
            catch (Exception ex) { Logger.Log("PresetManager", $"List failed: {ex.Message}"); return Array.Empty<string>(); }
        }

        public static bool Exists(string name) => File.Exists(PathFor(name));

        /// <summary>Load a preset by display name → its CaptureSettings, or null if missing/corrupt.</summary>
        public static CaptureSettings? Load(string name)
        {
            try
            {
                var path = PathFor(name);
                if (!File.Exists(path)) return null;
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<CaptureSettings>(File.ReadAllText(path), opts);
            }
            catch (Exception ex) { Logger.Log("PresetManager", $"Load '{name}' failed: {ex.Message}"); return null; }
        }

        /// <summary>
        /// Save `live` (identity-stripped) as a preset. When <paramref name="overwrite"/> is false and the
        /// name is taken, a " (2)" suffix is appended. Returns the display name actually written.
        /// </summary>
        public static string Save(string name, CaptureSettings live, bool overwrite)
        {
            Directory.CreateDirectory(PresetsDir);
            string display = SessionManager.SanitizeFolderName(name);
            if (!overwrite)
            {
                string baseName = display;
                for (int n = 2; Exists(display); n++) display = $"{baseName} ({n})";
            }
            WriteAtomic(PathFor(display), StripIdentity(live));
            return display;
        }

        public static void Delete(string name)
        {
            try { var p = PathFor(name); if (File.Exists(p)) File.Delete(p); }
            catch (Exception ex) { Logger.Log("PresetManager", $"Delete '{name}' failed: {ex.Message}"); }
        }

        /// <summary>Rename a preset (de-dupes the target); returns the final display name.</summary>
        public static string Rename(string oldName, string newName)
        {
            string target = SessionManager.SanitizeFolderName(newName);
            if (string.Equals(target, oldName, StringComparison.OrdinalIgnoreCase)) return oldName;
            string baseName = target;
            for (int n = 2; Exists(target); n++) target = $"{baseName} ({n})";
            try
            {
                if (File.Exists(PathFor(oldName))) File.Move(PathFor(oldName), PathFor(target));
                return target;
            }
            catch (Exception ex) { Logger.Log("PresetManager", $"Rename '{oldName}' failed: {ex.Message}"); return oldName; }
        }

        private static string PathFor(string name) =>
            Path.Combine(PresetsDir, SessionManager.SanitizeFolderName(name) + ".json");

        private static void WriteAtomic(string path, CaptureSettings s)
        {
            try
            {
                var json = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
            }
            catch (Exception ex) { Logger.Log("PresetManager", $"Write '{path}' failed: {ex.Message}"); }
        }
    }
}
