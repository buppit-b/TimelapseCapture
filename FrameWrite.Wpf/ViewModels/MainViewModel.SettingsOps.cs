using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using FrameWrite; // Core: settings, sessions, ffmpeg, capture engine, screen helper

namespace FrameWrite.Wpf.ViewModels
{
    /// <summary>
    /// MainViewModel — settings operations: Settings/Overlay/Wizard dialogs (incl. the retroactive
    /// bake), pre-destructive-op backup, presets, export/import, restore defaults.
    /// </summary>
    public partial class MainViewModel
    {

        private void OpenSettings()
        {
            var dlg = new SettingsDialog { Owner = Application.Current?.MainWindow, DataContext = this };
            dlg.ShowDialog();
        }

        // Where the most recent safety backup landed (so the op that follows can tell the user).
        private string? _lastBackupPath;

        // Runs the offered pre-destructive-op backup behind the caller's busy flag. False = backup
        // failed and the destructive operation must NOT proceed (nothing has been changed yet).
        // BackupSession itself refuses (throws) if the drive can't hold the copy + a 256 MB margin.
        private async Task<bool> BackupSessionForSafety(string folder)
        {
            _lastBackupPath = null;
            EncodeStatus = "Backing up session…";
            try
            {
                string dest = await Task.Run(() => SessionManager.BackupSession(folder,
                    (i, total) =>
                    {
                        if (i % 50 == 0 || i == total)
                            Application.Current?.Dispatcher.BeginInvoke(
                                () => EncodeStatus = $"Backing up… {i}/{total}");
                    }));
                _lastBackupPath = dest;
                Logger.Log("Wpf", $"Session backed up to {dest} before a destructive operation.");
                return true;
            }
            catch (Exception ex)
            {
                EncodeStatus = "Backup failed — nothing was changed";
                MessageDialog.Show($"Couldn't back up the session, so nothing was changed:\n{ex.Message}",
                    "Backup", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // Tell the user where a just-made safety backup landed and offer to open it — they otherwise
        // have no way to find it. No-op if the op didn't back up.
        private void NotifyBackupLocationIfAny()
        {
            var path = _lastBackupPath;
            if (path == null) return;
            _lastBackupPath = null;
            int c = MessageDialog.ShowChoices(
                $"A backup of the original frames was saved to:\n\n{path}",
                "Backup saved", MessageBoxImage.Information, "Open backup folder", "Close");
            if (c == 0) { try { System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\""); } catch { } }
        }

        private async Task OpenOverlay()
        {
            var dlg = new OverlayDialog { Owner = Application.Current?.MainWindow, DataContext = this };
            dlg.ShowDialog();
            if (!dlg.BakeRequested || _sessionFolder == null) return;
            // The global hotkey / tray can start a capture while the dialog is open (modality blocks
            // input, not posted messages). Refusing is right — but a bake the user CONFIRMED must never
            // vanish silently ("failures surfaced, never silent").
            if (IsCapturing || IsEncoding)
            {
                Logger.Log("Wpf", "Overlay bake skipped: capture/encode started while the dialog was open.");
                MessageDialog.Show(
                    "The bake didn't run — capture started while the overlay dialog was open.\n\n" +
                    "Nothing was changed: no backup was made, no frames were rewritten. Stop capture, " +
                    "then bake again from the Overlay dialog.",
                    "Bake overlay", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Retroactive bake — re-writes every frame on disk (consent already given in the dialog).
            // Same busy pattern as the destructive crop: IsEncoding gates start/encode/trim/cull/switch.
            IsEncoding = true;
            EncodeStatus = "Baking overlay…";
            try
            {
                var folder = _sessionFolder;
                if (dlg.BackupFirstRequested && !await BackupSessionForSafety(folder)) return;
                EncodeStatus = "Baking overlay…";
                var overlay = BuildOverlay();
                int done = await Task.Run(() => SessionManager.BakeOverlay(
                    folder, overlay, _settings.JpegQuality,
                    (i, total) =>
                    {
                        if (i % 25 == 0 || i == total)
                            Application.Current?.Dispatcher.BeginInvoke(
                                () => EncodeStatus = $"Baking overlay… {i}/{total}");
                    }));
                EncodeStatus = $"Overlay baked into {done} frame(s) ✓";
                Logger.Log("Wpf", $"Retroactive overlay bake: {done} frame(s) in {folder}.");
            }
            catch (Exception ex)
            {
                EncodeStatus = "Overlay bake failed";
                MessageDialog.Show($"Couldn't bake the overlay into the frames:\n{ex.Message}",
                    "Bake overlay", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { IsEncoding = false; }
            UpdatePreview();          // the frames' pixels changed — show it
            NotifyBackupLocationIfAny();   // tell the user where the safety backup went
        }

        /// <summary>Guided setup — shown automatically on first run, re-runnable from Settings.</summary>
        public void OpenWizard()
        {
            var dlg = new SetupWizard { Owner = Application.Current?.MainWindow, DataContext = this };
            dlg.ShowDialog();
        }

        // Open the diagnostics log (or its folder if it doesn't exist yet) — observability for "what happened?".
        private void OpenLog()
        {
            try
            {
                string path = Logger.FilePath;
                string open = File.Exists(path) ? path : (Path.GetDirectoryName(path) ?? path);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = open, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageDialog.Show($"Couldn't open the log: {ex.Message}", "Open log", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExportSettings()
        {
            var dlg = new SaveFileDialog { Title = "Export settings", Filter = "Settings (*.json)|*.json", FileName = "timelapse-settings.json" };
            if (dlg.ShowDialog() != true) return;
            try { SettingsManager.ExportTo(_settings, dlg.FileName); }
            catch (Exception ex) { MessageDialog.Show($"Couldn't export settings: {ex.Message}", "Export settings", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        // ---- Presets (named capture/encode/look setups; identity + safety fields never travel) ----
        public ICommand ApplyPresetCommand { get; }
        public ICommand SavePresetCommand { get; }
        public ICommand RenamePresetCommand { get; }
        public ICommand DeletePresetCommand { get; }
        public ICommand RestoreDefaultsCommand { get; }

        public System.Collections.ObjectModel.ObservableCollection<string> Presets { get; } = new();

        private string? _selectedPreset;
        public string? SelectedPreset
        {
            get => _selectedPreset;
            set { if (_selectedPreset != value) { _selectedPreset = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); } }
        }

        private void RefreshPresets()
        {
            string? keep = SelectedPreset;
            Presets.Clear();
            foreach (var n in PresetManager.List()) Presets.Add(n);
            SelectedPreset = keep != null && Presets.Contains(keep) ? keep : null;
        }

        private void ApplyPreset()
        {
            if (SelectedPreset == null || IsCapturing) return;
            var preset = PresetManager.Load(SelectedPreset);
            if (preset == null) { MessageDialog.Show("That preset couldn't be loaded.", "Presets", MessageBoxButton.OK, MessageBoxImage.Warning); RefreshPresets(); return; }

            var merged = PresetManager.ApplyOnto(preset, _settings);
            NormalizeSettings(merged);   // re-clamp untrusted file values, same as Import

            // Guard the image2 uniformity invariant: switching format on a session that already has frames
            // would mix file types and block encoding. Warn before applying (mirrors the UsePng warning).
            if (_frameCount > 0 && !string.Equals(merged.Format, _settings.Format, StringComparison.OrdinalIgnoreCase))
            {
                var r = MessageDialog.Show(
                    $"“{SelectedPreset}” captures as {merged.Format}, but this session already has {_frameCount} {_settings.Format} frame(s). Applying it would mix formats and block encoding until you cull/convert.\n\nApply anyway?",
                    "Presets", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) return;
            }

            _settings = merged;
            SettingsManager.Save(_settings);
            ThemeManager.Apply(_settings.Theme);   // theme is carried → apply live
            OnPropertyChanged(string.Empty);       // rebind every setting-backed property
            WindowAffinityChanged?.Invoke();        // HideFromCapture may have changed
            NotifyOverlayDerived();
            RefreshStats(); BumpRecalc();           // recompute storage/length/progress from the new fps/skip/format
        }

        private void SavePreset()
        {
            var dlg = new TextPromptDialog("Save preset", "Preset name", SelectedPreset ?? "My setup")
            { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true) return;
            string name = string.IsNullOrWhiteSpace(dlg.Value) ? "My setup" : dlg.Value!.Trim();

            bool overwrite = false;
            if (PresetManager.Exists(name))
            {
                var r = MessageDialog.Show($"A preset named “{name}” already exists. Overwrite it with the current settings?",
                    "Save preset", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;   // decline → cancel (Save-as-new would need a different name)
                overwrite = true;
            }
            string saved = PresetManager.Save(name, _settings, overwrite);
            RefreshPresets();
            SelectedPreset = saved;
        }

        private void RenamePreset()
        {
            if (SelectedPreset == null) return;
            var dlg = new TextPromptDialog("Rename preset", "New name", SelectedPreset)
            { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value)) return;
            string final = PresetManager.Rename(SelectedPreset, dlg.Value!.Trim());
            RefreshPresets();
            SelectedPreset = final;
        }

        private void DeletePreset()
        {
            if (SelectedPreset == null) return;
            var r = MessageDialog.Show($"Delete the preset “{SelectedPreset}”? This can't be undone.",
                "Delete preset", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
            PresetManager.Delete(SelectedPreset);
            SelectedPreset = null;
            RefreshPresets();
        }

        // A safe way back if the user messes something up (a broken output-name template, odd encode
        // values, a theme they can't undo). Resets everything to defaults EXCEPT the machine paths
        // (output folder + ffmpeg) and the first-run flag — so it doesn't strand them or re-nag the wizard.
        private void RestoreDefaults()
        {
            if (IsCapturing) return;
            var r = MessageDialog.Show(
                "Reset all settings to their defaults?\n\nYour output folder and FFmpeg location are kept. Everything else — interval, format, encoding, overlay, theme, safety limits, hotkey, output-name template — returns to a safe default. Saved presets are not affected.",
                "Restore defaults", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;

            _settings = new CaptureSettings
            {
                SaveFolder = _settings.SaveFolder,
                FfmpegPath = _settings.FfmpegPath,
                FirstRunCompleted = _settings.FirstRunCompleted,   // don't re-trigger onboarding
            };
            SettingsManager.Save(_settings);
            ThemeManager.Apply(_settings.Theme);
            OnPropertyChanged(string.Empty);   // rebind everything
            WindowAffinityChanged?.Invoke();
            HotkeysChanged?.Invoke();           // hotkey returns to its default (disabled)
            NotifyOverlayDerived();
            RefreshStats(); BumpRecalc();
        }

        private void ImportSettings()
        {
            var dlg = new OpenFileDialog { Title = "Import settings", Filter = "Settings (*.json)|*.json" };
            if (dlg.ShowDialog() != true) return;
            var imported = SettingsManager.LoadFrom(dlg.FileName);
            if (imported == null)
            {
                MessageDialog.Show("That file isn't a valid settings file.", "Import settings", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            NormalizeSettings(imported);   // an imported file bypasses the property-setter clamps — re-bound here
            _settings = imported;
            SettingsManager.Save(_settings);
            // Resync cached display state the blanket notify can't recompute (they have backing fields).
            OutputFolder = string.IsNullOrWhiteSpace(_settings.SaveFolder) ? "(not set)" : _settings.SaveFolder!;
            RefreshOutputFolderMissing();
            OnPropertyChanged(string.Empty); // refresh every binding against the new settings
            RefreshStats(); BumpRecalc();    // recompute storage/length/progress from the imported fps/skip/format
            // Side effects the blanket notify can't reach: imported hotkey bindings must re-register
            // with Win32 NOW (they used to sit inert until restart), and hide-from-capture re-applies.
            HotkeysChanged?.Invoke();
            WindowAffinityChanged?.Invoke();
        }

        // Clamp fields that aren't already re-clamped at point of use, so a hand-edited/foreign settings.json
        // can't push out-of-range values into the app. (JpegQuality/intervals/CRF are re-clamped in the engine/
        // encoder; EncodePreset is allowlisted in VideoEncoder — those don't need re-clamping here.)
        private static void NormalizeSettings(CaptureSettings s)
        {
            s.JpegQuality = Math.Clamp(s.JpegQuality, 1, 100);
            s.OverlayPosition = Math.Clamp(s.OverlayPosition, 0, 3);
            s.TrackResizeMode = Math.Clamp(s.TrackResizeMode, 0, 2);
            s.LowDiskStopMB = Math.Max(Constants.EmergencyDiskFloorMB, s.LowDiskStopMB);
            s.MaxDurationMinutes = Math.Max(1, s.MaxDurationMinutes);
            s.StopAtStorageMB = Math.Max(10, s.StopAtStorageMB);
            s.EncodeEveryNth = Math.Clamp(s.EncodeEveryNth, 1, 1000);
            s.EncodeFps = Math.Clamp(s.EncodeFps, 1, 240);
            s.EncodeCrf = Math.Clamp(s.EncodeCrf, 0, 51);
            s.EncodeHoldLastSeconds = Math.Clamp(s.EncodeHoldLastSeconds, 0, 60);
            s.OverlayCustomX = s.OverlayCustomX < 0 ? -1 : Math.Min(1, s.OverlayCustomX);
            s.OverlayCustomY = s.OverlayCustomY < 0 ? -1 : Math.Min(1, s.OverlayCustomY);
            if (s.IntervalSecondsExact > 0)
                s.IntervalSecondsExact = Math.Clamp(s.IntervalSecondsExact, 0.1m, 3600m);
            if (s.Format != "JPEG" && s.Format != "PNG") s.Format = "JPEG";
        }
    }
}
