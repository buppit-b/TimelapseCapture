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
    /// MainViewModel — the capture region: select/edit/full-screen/track sources, the on-screen
    /// outline (incl. tracked-window following), and region persistence. Every source funnels
    /// through ApplyRegion.
    /// </summary>
    public partial class MainViewModel
    {

        private bool ConfirmRegionChange()
        {
            if (_session != null && _frameCount > 0)
            {
                var canonical = _sessionFolder != null ? SessionManager.GetFrameSize(_sessionFolder) : System.Drawing.Size.Empty;
                // Only promise scaling when we could actually read the canonical size — if the first
                // frame is unreadable, StartEngine can't arm scaling and sizes could genuinely mix.
                if (canonical.Width >= 2)
                {
                    // Benign case (new source auto-scales to match) — the repeat-prone prompt gets a
                    // "don't ask again" way out. The unreadable-canonical case below stays a hard ask.
                    return Prompts.Confirm(_settings, "region-change-scaled",
                        $"This session already has {_frameCount} frame(s).\n\n" +
                        $"This session's frames are {canonical.Width}×{canonical.Height}. A different-sized selection or tracked window " +
                        "will be SCALED to match (letterboxed if the shape differs) so the video stays consistent — scaling costs a " +
                        "little sharpness.\n\nChange the region?",
                        "Change region?");
                }
                var r = MessageDialog.Show(
                    $"This session already has {_frameCount} frame(s).\n\n" +
                    "The existing frames' size couldn't be read, so a different-sized selection may MIX frame sizes and break the " +
                    "final encode.\n\nChange the region?",
                    "Change region?", MessageBoxButton.YesNo, MessageBoxImage.Question);
                return r == MessageBoxResult.Yes;
            }
            return true;
        }

        private void ToggleOverlay()
        {
            if (_isOverlayShown)
            {
                _overlay?.Close();
                _overlay = null;
                IsOverlayShown = false;
            }
            else if (_region.HasValue)
            {
                _overlay ??= new RegionOverlay();
                _overlay.ShowForRegion(_region.Value);
                IsOverlayShown = true;
            }
            SyncTrackOverlay();
        }

        private void UpdateOverlay()
        {
            if (!_isOverlayShown) return;
            if (_region.HasValue && _overlay != null)
                _overlay.ShowForRegion(_region.Value);
            else
            {
                _overlay?.Close();
                _overlay = null;
                IsOverlayShown = false;
            }
            SyncTrackOverlay();
        }

        // Start/stop following the on-screen outline to the tracked window (only while the outline is shown
        // and a window is being tracked). The engine's capture already follows; this keeps the visible
        // outline in lock-step so it stays just-outside the captured region and never lands in a frame.
        private System.Drawing.Rectangle _lastOverlayRect;
        private void SyncTrackOverlay()
        {
            if (_isOverlayShown && _trackedWindow != IntPtr.Zero && _overlay != null)
            {
                if (!_trackOverlayTimer.IsEnabled)
                {
                    _lastOverlayRect = System.Drawing.Rectangle.Empty;   // force the first tick to position it
                    _trackOverlayTimer.Start();
                }
            }
            else
            {
                _trackOverlayTimer.Stop();
            }
        }

        private void OnTrackOverlayTick(object? sender, EventArgs e)
        {
            if (!_isOverlayShown || _trackedWindow == IntPtr.Zero || _overlay == null || !_region.HasValue)
            {
                _trackOverlayTimer.Stop();
                return;
            }
            bool ok = WindowEnumerator.TryGetLiveBounds(_trackedWindow, out var b, out bool minimized, out bool alive);
            if (!alive) { _trackOverlayTimer.Stop(); return; }   // window gone → don't poll a dead HWND forever
            if (!ok || minimized) return;                        // transient / hidden → skip this tick, keep polling

            System.Drawing.Rectangle rect;
            if (_settings.TrackResizeMode == 0)   // lock size: outline is the locked box at the window's top-left
            {
                var locked = _region.Value;
                var candidate = new System.Drawing.Rectangle(b.X, b.Y, locked.Width, locked.Height);
                rect = ScreenHelper.FitRegionOnScreen(candidate, out _) ?? locked;
            }
            else                                  // scale modes: outline tracks the whole window (follows resize)
            {
                rect = b;
            }

            if (rect == _lastOverlayRect) return;   // unchanged → don't relayout the overlay window
            _lastOverlayRect = rect;
            _overlay.ShowForRegion(rect);
        }

        private void SelectFullScreen()
        {
            var monitors = ScreenHelper.Monitors();
            System.Drawing.Rectangle r;
            if (monitors.Count > 1)
            {
                var dlg = new MonitorPickerDialog(monitors) { Owner = Application.Current?.MainWindow };
                if (dlg.ShowDialog() != true || dlg.SelectedBounds == null) return;
                r = dlg.SelectedBounds.Value;
            }
            else
            {
                r = monitors[0].Bounds;
            }

            r.Width -= r.Width % 2;   // even dimensions required by the H.264 encoder
            r.Height -= r.Height % 2;

            // No change → no warning. Only prompt (about mixing frame sizes) if the region actually differs.
            if (!RegionEquals(_region, r) && !ConfirmRegionChange()) return;
            ApplyRegion(r, $"{r.Width}×{r.Height} (full screen)");
        }

        private static bool RegionEquals(System.Drawing.Rectangle? a, System.Drawing.Rectangle b)
            => a.HasValue && a.Value == b;

        // Pick a top-level window; capture follows it as it moves (size locked at the current size).
        private void TrackWindow()
        {
            if (!ConfirmRegionChange()) return;
            var dlg = new WindowPickerDialog { Owner = Application.Current?.MainWindow };
            if (dlg.ShowDialog() != true) return;
            if (!WindowEnumerator.TryGetLiveBounds(dlg.SelectedHwnd, out var b, out _, out bool alive) || !alive) return;

            int w = b.Width - b.Width % 2;     // even dims for the H.264 encoder
            int h = b.Height - b.Height % 2;
            if (w < 2 || h < 2) return;

            var r = new Rectangle(b.X, b.Y, w, h);
            ApplyRegion(r, $"{w}×{h} · tracking “{dlg.SelectedTitle}” (follows)", dlg.SelectedHwnd);
            // The ratio lock doesn't drive a tracked window (its size comes from the window) — reset the
            // segs to Free so the UI doesn't imply a constraint that isn't being applied.
            AspectRatioIndex = 0;
            RatioFlipped = false;
        }

        private void SelectRegion()
        {
            if (!ConfirmRegionChange()) return;
            var (rw, rh) = EffectiveRatio();
            var overlay = new RegionSelectOverlay(rw, rh);
            if (ShowRegionOverlay(overlay) == true && overlay.SelectedRegion.HasValue)
                ApplyRegion(overlay.SelectedRegion.Value);
        }

        private void EditRegion()
        {
            if (!_region.HasValue) return;
            if (!ConfirmRegionChange()) return;
            var (rw, rh) = EffectiveRatio();
            var dlg = new RegionEditOverlay(_region.Value, rw, rh);
            if (ShowRegionOverlay(dlg) == true && dlg.SelectedRegion.HasValue)
                ApplyRegion(dlg.SelectedRegion.Value);
        }

        // Show a full-screen region overlay, optionally hiding the main window first so it doesn't
        // block the very thing the user is trying to select (default on). Keeps the wizard as owner
        // when a pick is launched from it; never owns the overlay by a window we've hidden.
        // Re-entrancy guard: the region-select global hotkey (and key mashing generally) fires even
        // while a picker is already on screen — a second full-screen overlay on top of the first is
        // pure confusion. One picker at a time.
        private bool _regionPickerOpen;

        private bool? ShowRegionOverlay(Window overlay)
        {
            if (_regionPickerOpen) return null;
            _regionPickerOpen = true;
            var main = Application.Current?.MainWindow;
            var active = ActiveWindow();
            if (active != null && active != main && active.IsVisible) overlay.Owner = active;

            bool hide = _settings.HideWindowDuringRegionSelect && main != null && main.IsVisible;
            if (hide) main!.Hide();
            try { return overlay.ShowDialog(); }
            finally
            {
                _regionPickerOpen = false;
                if (hide) { main!.Show(); main.Activate(); }
            }
        }

        // The window that should own a transient dialog: the active one (e.g. the modal setup wizard
        // when a region pick is launched from it), falling back to the main window. Correct ownership
        // fixes z-order and foreground activation for nested modals.
        private static Window? ActiveWindow()
        {
            var app = Application.Current;
            if (app == null) return null;
            return app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? app.MainWindow;
        }

        private void ApplyRegion(System.Drawing.Rectangle r, string? label = null, IntPtr trackedWindow = default)
        {
            _trackedWindow = trackedWindow;   // every region source funnels here, so static sources reset to 0
            _region = r;
            RegionText = label ?? $"{r.Width}×{r.Height} at ({r.X},{r.Y})";
            RefreshRegionScaleSuffix();   // every source (incl. full screen / tracking) shows the scale note
            PersistRegion(r);
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(RegionNeeded));
            CommandManager.InvalidateRequerySuggested();
            UpdateOverlay();
        }

        // Re-evaluate the "→ scaled to W×H" tail of RegionText against the CURRENT canonical frame
        // size (it changes after e.g. a destructive crop): strip any prior suffix, re-append on mismatch.
        private void RefreshRegionScaleSuffix()
        {
            if (!_region.HasValue) return;
            int i = RegionText.IndexOf(" → scaled to", StringComparison.Ordinal);
            string baseText = i >= 0 ? RegionText[..i] : RegionText;
            string suffix = "";
            if (_frameCount > 0 && _sessionFolder != null)
            {
                var canonical = SessionManager.GetFrameSize(_sessionFolder);
                if (canonical.Width >= 2 && canonical != _region.Value.Size)
                    suffix = $" → scaled to {canonical.Width}×{canonical.Height}";
            }
            RegionText = baseText + suffix;
        }

        // The region is part of a session's identity (all its frames are that size and place), so save
        // it with the session — loading restores it and a continued session keeps the same area.
        private void PersistRegion(System.Drawing.Rectangle r)
        {
            if (_session == null || _sessionFolder == null) return;
            try
            {
                var s = SessionManager.LoadSession(_sessionFolder) ?? _session;
                s.CaptureRegion = r;
                SessionManager.SaveSession(_sessionFolder, s);
                _session = s;
            }
            catch { /* best-effort; never throw from a region change */ }
        }
    }
}
