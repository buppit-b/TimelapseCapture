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
    /// MainViewModel — session lifecycle: output folder, new/load/rename, crash recovery, and the
    /// Active-flag bookkeeping.
    /// </summary>
    public partial class MainViewModel
    {
        private void ChooseFolder()
        {
            var dlg = new OpenFolderDialog { Title = "Select output folder for captures" };
            // Open at the current folder — or its nearest ancestor that still exists (it may be deleted).
            string? seed = _settings.SaveFolder;
            while (!string.IsNullOrWhiteSpace(seed) && !Directory.Exists(seed))
                seed = Path.GetDirectoryName(seed);
            if (!string.IsNullOrWhiteSpace(seed)) dlg.InitialDirectory = seed;

            if (dlg.ShowDialog() == true)
            {
                _settings.SaveFolder = dlg.FolderName;
                SettingsManager.Save(_settings);
                OutputFolder = dlg.FolderName;
                RefreshOutputFolderMissing();
                OnPropertyChanged(nameof(HasSaveFolderSet));
                OnPropertyChanged(nameof(CapturesRootHint));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void NewSession()
        {
            // Guard against losing your place with a stray click: if the current session already
            // has frames, confirm before switching away (the old one stays safe on disk).
            if (_session != null && _frameCount > 0 && !IsCapturing)
            {
                var r = MessageDialog.Show(
                    $"The current session “{SessionName}” has {_frameCount} frame(s).\n\nIt will be kept on disk, but a new session will replace it here. Start a new session?",
                    "New session?", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
            }

            // If the current session has no frames, recycle its folder instead of spawning another empty
            // one — but still make it a genuine fresh session (new default name + cleared region/target),
            // so "New Session" always behaves like a new session, not a rename.
            bool reuseEmpty = _session != null && _sessionFolder != null && _frameCount == 0 && !IsCapturing;

            // Name it up front (prefilled with a fresh default — Enter accepts it; Cancel aborts).
            string defaultName = $"Session_{DateTime.Now:yyyyMMdd_HHmmss}";
            var dlg = new TextPromptDialog("New session", "Session name", defaultName)
            { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true) return;
            string name = string.IsNullOrWhiteSpace(dlg.Value) ? defaultName : dlg.Value.Trim();

            if (reuseEmpty)
            {
                if (!string.Equals(name, _session!.Name, StringComparison.Ordinal))
                    ApplySessionName(name);   // rename the recycled folder
                ResetToFreshSession();        // clean slate: clear region/tracking/overlay/target
                return;
            }
            CreateSession(name);
        }

        // Reset the runtime capture state to a clean, empty-session slate (shared by CreateSession and
        // the recycle-empty path). Does NOT touch the session folder/name.
        private void ResetToFreshSession()
        {
            _region = null;
            _trackedWindow = IntPtr.Zero;
            _accumulatedSeconds = 0;
            _timerRunBase = 0;                 // fresh session — the run clock starts from nothing
            ResetTarget();
            ResetCadence();                    // clear the sparkline trace — different frame history
            PreviewImage = null;
            ClearCaptureError();
            RegionText = "Not selected";
            FrameCount = (int)(_session?.FramesCaptured ?? 0);
            UpdateOverlay();
            RefreshCropInfo();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(RegionNeeded));
            OnPropertyChanged(nameof(SessionNeeded));
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>Create a default-named session if none exists — used by the setup wizard (no prompt mid-flow).</summary>
        public void EnsureDefaultSession()
        {
            if (!SessionNeeded || !HasOutputFolder || IsCapturing) return;
            CreateSession($"Session_{DateTime.Now:yyyyMMdd_HHmmss}");
        }

        private void CreateSession(string name)
        {
            try
            {
                string capturesRoot = Path.Combine(_settings.SaveFolder!, "captures");
                _sessionFolder = SessionManager.CreateNamedSession(
                    capturesRoot, name, _settings.IntervalSeconds, null, _settings.Format ?? "JPEG", _settings.JpegQuality);
                _session = SessionManager.LoadSession(_sessionFolder);
                SessionName = _session?.Name ?? name;
                ResetToFreshSession();   // clean slate (region/tracking/overlay/target/preview/frame count)
            }
            catch (Exception ex)
            {
                MessageDialog.Show($"Failed to create session:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadSession()
        {
            string capturesRoot = Path.Combine(_settings.SaveFolder ?? "", "captures");
            var dlg = new LoadSessionDialog(capturesRoot, _sessionFolder,
                FfmpegRunner.FindFfmpeg(_settings.FfmpegPath), this) { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true || dlg.SelectedFolder == null) return;
            LoadSessionFromFolder(dlg.SelectedFolder, fromPicker: true);
        }

        private void LoadSessionFromFolder(string folder, bool fromPicker)
        {
            var session = SessionManager.LoadSession(folder);
            if (session == null)
            {
                if (fromPicker)
                    MessageDialog.Show("That folder doesn't contain a valid session (no session.json).",
                        "Load Session", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // Every load path funnels through here, so this single gate keeps an archived session
            // (frames packed into archive.mkv, none on disk) out of ALL live state — capture would
            // interleave new frames next to the archive, encode/preview would see nothing.
            if (session.Archived)
            {
                if (fromPicker)   // the picker normally restores first; this catches stray paths
                    MessageDialog.Show("This session is archived — use Load Session and restore it first.",
                        "Load Session", MessageBoxButton.OK, MessageBoxImage.Warning);
                else Logger.Log("Wpf", $"Skipped loading archived session: {folder}");
                return;
            }

            _session = session;
            _sessionFolder = folder;
            _trackedWindow = IntPtr.Zero;   // a loaded session is a static region (tracking isn't persisted)
            ClearCaptureError();   // loading a session clears any leftover warning

            // Restore the saved region. Keep its exact size; if its saved spot is no longer on any
            // monitor (display unplugged / resolution changed), relocate it onto the current desktop
            // rather than lose it — the size must stay constant to keep this session's frames uniform.
            _region = session.CaptureRegion;
            bool regionMoved = false, regionCantFit = false;
            if (_region.HasValue)
            {
                // A region from an older build / hand-edited / foreign session.json isn't guaranteed even or
                // sane. Force even dims (H.264) and reject a degenerate one, matching the fresh-selection path.
                var raw = _region.Value;
                raw.Width -= raw.Width % 2;
                raw.Height -= raw.Height % 2;
                if (raw.Width < 2 || raw.Height < 2)
                    _region = null;   // unusable → treated as "not selected" below
                else
                    _region = ScreenHelper.FitRegionOnScreen(raw, out regionMoved);
                regionCantFit = _region == null && raw.Width >= 2 && raw.Height >= 2;
            }

            _accumulatedSeconds = session.TotalCaptureSeconds; // restore cumulative capture time
            _timerRunBase = _accumulatedSeconds;   // run clock reads 00:00 until capture starts here
            ResetTarget();   // target isn't per-session — reset, don't carry over
            ResetCadence();  // clear the sparkline trace — this session has its own frame history
            SessionName = session.Name ?? "Session";
            if (_region.HasValue)
            {
                var r = _region.Value;
                RegionText = regionMoved
                    ? $"{r.Width}×{r.Height} at ({r.X},{r.Y}) — moved onto screen"
                    : $"{r.Width}×{r.Height} at ({r.X},{r.Y})";
            }
            else
            {
                RegionText = regionCantFit
                    ? (session.FramesCaptured > 0
                        ? "Saved region doesn't fit this display — select any area; it'll be scaled to match this session's frames"
                        : "Saved region doesn't fit this display — select again")
                    : "Not selected";
            }
            FrameCount = (int)session.FramesCaptured;
            UpdateOverlay();   // refresh the on-screen outline to the loaded region (or close it if none)
            RefreshCropInfo(); // the loaded session may carry a saved encode-crop
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(RegionNeeded));
            OnPropertyChanged(nameof(SessionNeeded));   // stop the New-Session pulse once a session is loaded
            CommandManager.InvalidateRequerySuggested();
            UpdatePreview();
        }

        public bool AlwaysOnTop
        {
            get => _settings.AlwaysOnTop;
            set { if (_settings.AlwaysOnTop != value) { _settings.AlwaysOnTop = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        // Opt-in "always-there recorder": on launch, continue the most recent session (or start a new
        // full-screen one) and begin capturing — so a work session can never be forgotten. Runs AFTER
        // crash recovery (posted later at the same dispatcher priority); recovery's loaded session, if
        // any, is exactly what this then starts. Every skip is logged — never a silent no-op.
        private void TryAutoStartOnLaunch()
        {
            if (!_settings.StartCaptureOnLaunch || IsCapturing || IsEncoding) return;
            if (string.IsNullOrWhiteSpace(_settings.SaveFolder)) return;   // true first run — the wizard owns this launch
            try
            {
                if (_session == null)
                {
                    // Continue the most recently touched session; a brand-new install starts fresh.
                    string root = Path.Combine(_settings.SaveFolder!, "captures");
                    string? best = Directory.Exists(root)
                        ? Directory.GetDirectories(root)
                            .Where(d => SessionManager.LoadSession(d) is { Archived: false })
                            .OrderByDescending(Directory.GetLastWriteTime)
                            .FirstOrDefault()
                        : null;
                    if (best != null) LoadSessionFromFolder(best, fromPicker: false);
                    // Direct create with a default name — NewSession() prompts for a name, and a
                    // modal dialog at sign-in would stall the zero-config auto-start it exists for.
                    else CreateSession($"Session_{DateTime.Now:yyyyMMdd_HHmmss}");
                }
                if (_session == null) { Logger.Log("Wpf", "Auto-start skipped: no session could be loaded or created."); return; }

                if (!_region.HasValue)
                {
                    // Zero-config default: the primary monitor, full screen — no picker dialogs at sign-in.
                    var monitors = ScreenHelper.Monitors();
                    var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
                    var r = primary.Bounds;
                    r.Width -= r.Width % 2;    // even dimensions required by the H.264 encoder
                    r.Height -= r.Height % 2;
                    ApplyRegion(r, $"{r.Width}×{r.Height} (full screen)");
                }

                if (_region.HasValue)
                {
                    Logger.Log("Wpf", $"Auto-start: capturing session “{SessionName}” on launch.");
                    StartCapture();
                }
                else Logger.Log("Wpf", "Auto-start skipped: no usable capture area.");
            }
            catch (Exception ex) { Logger.Log("Wpf", $"Auto-start failed: {ex.Message}"); }
        }

        // Crash recovery: a session left Active means the app died mid-capture. On launch, offer to
        // resume the most-recently-touched such session; clear the flag on all of them either way.
        private void CheckForInterruptedSession()
        {
            if (_session != null || IsCapturing || string.IsNullOrEmpty(_settings.SaveFolder)) return;
            try
            {
                string capturesRoot = Path.Combine(_settings.SaveFolder!, "captures");
                if (!Directory.Exists(capturesRoot)) return;

                string? best = null;
                SessionInfo? bestInfo = null;
                var actives = new List<string>();
                foreach (var dir in Directory.GetDirectories(capturesRoot))
                {
                    var s = SessionManager.LoadSession(dir);
                    if (s == null || !s.Active || s.FramesCaptured <= 0 || s.Archived) continue;
                    actives.Add(dir);
                    if (best == null || Directory.GetLastWriteTime(dir) > Directory.GetLastWriteTime(best))
                    {
                        best = dir;
                        bestInfo = s;
                    }
                }
                if (best == null || bestInfo == null) return;

                var r = MessageDialog.Show(
                    $"The session “{bestInfo.Name ?? Path.GetFileName(best)}” was still recording when the app " +
                    $"last closed ({bestInfo.FramesCaptured} frame{(bestInfo.FramesCaptured == 1 ? "" : "s")}).\n\nResume it?",
                    "Resume interrupted session?", MessageBoxButton.YesNo, MessageBoxImage.Question);

                // Clear Active on every candidate so we don't keep prompting; the next Start re-marks it.
                foreach (var dir in actives) ClearActive(dir);
                if (r == MessageBoxResult.Yes) LoadSessionFromFolder(best, fromPicker: false);
            }
            catch { /* recovery is best-effort */ }
        }

        private static void ClearActive(string folder)
        {
            try
            {
                var s = SessionManager.LoadSession(folder);
                if (s == null) return;
                s.Active = false;
                SessionManager.SaveSession(folder, s);
            }
            catch { /* best-effort */ }
        }

        // Toggle the session's Active flag on disk (capture lifecycle: true on start, false on clean stop).
        private void SetSessionActive(bool active)
        {
            if (_sessionFolder == null) return;
            try
            {
                var s = SessionManager.LoadSession(_sessionFolder);
                if (s == null) return;
                s.Active = active;
                // Stamp the ACTUAL interval when a run begins (active=true): session.json's int
                // IntervalSeconds is a rounded creation-time value that can't show 0.1s / 3.1s.
                if (active) s.IntervalSecondsActual = (double)IntervalSeconds;
                SessionManager.SaveSession(_sessionFolder, s);
                _session = s;
            }
            catch { /* best-effort */ }
        }

        /// <summary>
        /// Window is closing: stop any capture cleanly (which marks the session inactive, so a
        /// deliberate close isn't mistaken for a crash next launch) and dispose the engine.
        /// </summary>
        public void OnAppClosing()
        {
            try
            {
                // The region outline is an UNOWNED window (deliberately — it must survive minimize-to-
                // tray). Left open, it outlived the app: with the default OnLastWindowClose shutdown it
                // kept the whole process alive as an orphan outline on screen. Close it explicitly
                // (ShutdownMode=OnMainWindowClose is the belt-and-braces systemic guarantee).
                _overlay?.Close();
                _overlay = null;
                // Kill in-flight external work: cancelling the encode CTS fires its registered
                // callback SYNCHRONOUSLY, which kills the spawned ffmpeg — otherwise closing
                // mid-encode left an invisible ffmpeg running (and writing) after the app exited.
                _encodeCts?.Cancel();
                _ffmpegCts?.Cancel();
                if (IsCapturing) StopCapture();
                _engine.Dispose();
            }
            catch { /* best-effort shutdown */ }
        }


        private void RenameSession()
        {
            if (_session == null || _sessionFolder == null) return;
            var dlg = new TextPromptDialog("Rename session", "Session name", _session.Name ?? "")
            {
                Owner = Application.Current?.MainWindow
            };
            if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.Value)) return;
            ApplySessionName(dlg.Value.Trim());
        }

        // Rename the current session: folder to match (sanitised + de-duplicated), display name verbatim.
        // Shared by RenameSession and the New Session name prompt (reusing an empty session).
        private void ApplySessionName(string newName)
        {
            if (_session == null || _sessionFolder == null || string.IsNullOrWhiteSpace(newName)) return;
            try
            {
                string? parent = Path.GetDirectoryName(_sessionFolder);
                string safe = SessionManager.SanitizeFolderName(newName);
                if (parent != null && safe.Length > 0 &&
                    !string.Equals(Path.GetFileName(_sessionFolder), safe, StringComparison.OrdinalIgnoreCase))
                {
                    string target = Path.Combine(parent, safe);
                    int n = 2;
                    while (Directory.Exists(target)) target = Path.Combine(parent, $"{safe} ({n++})");
                    Directory.Move(_sessionFolder, target);
                    _sessionFolder = target;
                }

                var s = SessionManager.LoadSession(_sessionFolder) ?? _session;
                s.Name = newName;                                // display name kept verbatim
                SessionManager.SaveSession(_sessionFolder, s);
                _session = s;
                SessionName = s.Name;
            }
            catch (Exception ex)
            {
                MessageDialog.Show($"Couldn't rename the session: {ex.Message}", "Rename",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
