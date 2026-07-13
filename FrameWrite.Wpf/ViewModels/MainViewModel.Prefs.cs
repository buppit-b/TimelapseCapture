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
    /// MainViewModel — settings-backed preferences: theme, window behaviour (tray/topmost/affinity),
    /// tracking options, unattended-safety caps, output naming, and the frame overlay (incl. colours).
    /// </summary>
    public partial class MainViewModel
    {
        public string Theme
        {
            get => _settings.Theme;
            set { if (_settings.Theme != value) { _settings.Theme = value; SettingsManager.Save(_settings); ThemeManager.Apply(value); OnPropertyChanged(); } }
        }

        /// <summary>
        /// The lowest interval the user may set: 0.01s (100 fps). Below 0.1s is video-recording
        /// territory (see <see cref="IsVideoRate"/>) — allowed, but resource-intensive and flagged.
        /// 0.01s is also the engine's real floor (10 ms timer), so it's the last honest number.
        /// </summary>
        public const decimal MinIntervalSeconds = 0.01m;

        public event Action? WindowAffinityChanged;
        public bool HideFromCapture
        {
            get => _settings.HideFromCapture;
            set { if (_settings.HideFromCapture != value) { _settings.HideFromCapture = value; SettingsManager.Save(_settings); OnPropertyChanged(); WindowAffinityChanged?.Invoke(); } }
        }

        public bool HideWindowDuringRegionSelect
        {
            get => _settings.HideWindowDuringRegionSelect;
            set { if (_settings.HideWindowDuringRegionSelect != value) { _settings.HideWindowDuringRegionSelect = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public bool MinimizeToTray
        {
            get => _settings.MinimizeToTray;
            set { if (_settings.MinimizeToTray != value) { _settings.MinimizeToTray = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public bool CloseToTray
        {
            get => _settings.CloseToTray;
            set { if (_settings.CloseToTray != value) { _settings.CloseToTray = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public bool SoundOnStartStop
        {
            get => _settings.SoundOnStartStop;
            set { if (_settings.SoundOnStartStop != value) { _settings.SoundOnStartStop = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        // Live-frame preview card — collapsed by default so it doesn't push the window taller for a
        // feature you don't always need at a glance. Expanding it refreshes the current frame.
        public bool PreviewExpanded
        {
            get => _settings.PreviewExpanded;
            set { if (_settings.PreviewExpanded != value) { _settings.PreviewExpanded = value; SettingsManager.Save(_settings); OnPropertyChanged(); if (value) UpdatePreview(); } }
        }

        // Audio cue on explicit start/stop (opt-in) — useful feedback when the window is hidden in the
        // tray. Auto-stops use the separate finish notification, so this only fires on user actions.
        private void PlayStartStopCue()
        {
            if (_settings.SoundOnStartStop)
                try { System.Media.SystemSounds.Beep.Play(); } catch { }
        }

        // Window tracking: when the tracked window is minimized, wait for it to be restored (true) instead
        // of stopping capture (false, default). Only affects tracking mode.
        public bool PauseOnTrackedMinimize
        {
            get => _settings.PauseOnTrackedMinimize;
            set { if (_settings.PauseOnTrackedMinimize != value) { _settings.PauseOnTrackedMinimize = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        // Window tracking: force the tracked window to stay on top while capturing (truly un-occluded).
        public bool KeepTrackedWindowOnTop
        {
            get => _settings.KeepTrackedWindowOnTop;
            set
            {
                if (_settings.KeepTrackedWindowOnTop == value) return;
                _settings.KeepTrackedWindowOnTop = value;
                SettingsManager.Save(_settings);
                // React live while actively capturing, so the toggle isn't a no-op mid-run (and turning
                // it OFF doesn't strand the window topmost until the next stop). Both calls self-guard.
                if (IsCapturing && !_isPaused) { if (value) PinTrackedWindow(); else UnpinTrackedWindow(); }
                OnPropertyChanged();
            }
        }

        // Auto-stop capture once the frame count reaches the Target (projected frames for the target length).
        public bool StopAtTarget
        {
            get => _settings.StopAtTarget;
            set { if (_settings.StopAtTarget != value) { _settings.StopAtTarget = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        // How a tracked window's resize is handled: 0 = lock size (crop), 1 = scale-to-fit (letterbox), 2 = stretch.
        public int TrackResizeMode
        {
            get => _settings.TrackResizeMode;
            set { if (_settings.TrackResizeMode != value) { _settings.TrackResizeMode = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        // Unattended safety: stop a run before the drive fills (a full disk fails writes + can disrupt
        // other apps). Always on now — the only knob is the threshold, floored at the emergency minimum.
        public int LowDiskStopMB
        {
            get => _settings.LowDiskStopMB;
            set { var v = Math.Max(Constants.EmergencyDiskFloorMB, value); if (_settings.LowDiskStopMB != v) { _settings.LowDiskStopMB = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        // Opt-in: stop after a maximum accumulated capture duration (a hard wall-clock cap for unattended runs).
        public bool MaxDurationEnabled
        {
            get => _settings.MaxDurationEnabled;
            set { if (_settings.MaxDurationEnabled != value) { _settings.MaxDurationEnabled = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }
        // Encode speed-up: keep 1 frame in every N (stored 1 = off). Non-destructive — frames stay on
        // disk. Surfaced as a checkbox + N field (the app's reveal pattern) so "off" is explicit
        // instead of a magic 1, and the visible field can honestly enforce N ≥ 2.
        public int EncodeEveryNth
        {
            get => _settings.EncodeEveryNth;
            set
            {
                var v = Math.Clamp(value, 1, 1000);
                if (_settings.EncodeEveryNth != v)
                {
                    _settings.EncodeEveryNth = v;
                    SettingsManager.Save(_settings);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SpeedUpEnabled));
                    OnPropertyChanged(nameof(SpeedUpN));
                    OnPropertyChanged(nameof(EncodeSummaryText));
                    RefreshStats();   // encoded-frame count changed → video length / implied fps update now, not next tick
                    BumpRecalc();
                }
            }
        }
        public bool SpeedUpEnabled
        {
            get => EncodeEveryNth > 1;
            set => EncodeEveryNth = value ? Math.Max(2, EncodeEveryNth) : 1;
        }
        public int SpeedUpN
        {
            get => Math.Max(2, EncodeEveryNth);
            set => EncodeEveryNth = Math.Clamp(value, 2, 1000);
        }

        public bool StopAtStorageEnabled
        {
            get => _settings.StopAtStorageEnabled;
            set { if (_settings.StopAtStorageEnabled != value) { _settings.StopAtStorageEnabled = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }
        public int StopAtStorageMB
        {
            get => _settings.StopAtStorageMB;
            set { var v = Math.Max(10, value); if (_settings.StopAtStorageMB != v) { _settings.StopAtStorageMB = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public int MaxDurationMinutes
        {
            get => _settings.MaxDurationMinutes;
            set { var v = Math.Max(1, value); if (_settings.MaxDurationMinutes != v) { _settings.MaxDurationMinutes = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        // Sound + taskbar flash when a capture auto-stops or an encode finishes (so you don't have to watch).
        public bool NotifyOnFinish
        {
            get => _settings.NotifyOnFinish;
            set { if (_settings.NotifyOnFinish != value) { _settings.NotifyOnFinish = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public event Action? FinishNotified;
        private void NotifyFinished()
        {
            if (_settings.NotifyOnFinish)
                Application.Current?.Dispatcher.BeginInvoke(new Action(() => FinishNotified?.Invoke()));
        }

        // Filename template for encoded/trimmed videos. Tokens resolved in ResolveOutputName().
        public string OutputNameTemplate
        {
            get => _settings.OutputNameTemplate;
            set { if (_settings.OutputNameTemplate != value) { _settings.OutputNameTemplate = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        private string ResolveOutputName()
        {
            var now = DateTime.Now;
            return (_settings.OutputNameTemplate ?? "")
                .Replace("{session}", SessionName ?? "")
                .Replace("{datetime}", now.ToString("yyyyMMdd_HHmmss"))
                .Replace("{date}", now.ToString("yyyyMMdd"))
                .Replace("{time}", now.ToString("HHmmss"));
        }

        public bool CaptureCursor
        {
            get => _settings.CaptureCursor;
            set { if (_settings.CaptureCursor != value) { _settings.CaptureCursor = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public bool OverlayTimestamp
        {
            get => _settings.OverlayTimestamp;
            set { if (_settings.OverlayTimestamp != value) { _settings.OverlayTimestamp = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public string OverlayText
        {
            get => _settings.OverlayText;
            set { if (_settings.OverlayText != value) { _settings.OverlayText = value ?? ""; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public int OverlayPosition
        {
            get => _settings.OverlayPosition;
            set
            {
                var v = value is < 0 or > 3 ? 3 : value;
                bool wasCustom = OverlayUsesCustom;
                // Choosing a corner exits free-placement mode.
                _settings.OverlayCustomX = -1; _settings.OverlayCustomY = -1;
                if (_settings.OverlayPosition != v || wasCustom)
                {
                    _settings.OverlayPosition = v;
                    SettingsManager.Save(_settings);
                    OnPropertyChanged();
                    NotifyOverlayDerived();
                }
            }
        }

        // Free placement (drag-to-place). CustomX/Y are the normalized (0..1) top-left of the text;
        // < 0 means "use the corner Position instead". The overlay dialog reads these for the preview.
        public double OverlayCustomX => _settings.OverlayCustomX;
        public double OverlayCustomY => _settings.OverlayCustomY;
        public bool OverlayUsesCustom => _settings.OverlayCustomX >= 0 && _settings.OverlayCustomY >= 0;

        /// <summary>Place the overlay freely (from a drag on the preview, or the X/Y fields).</summary>
        public void SetOverlayCustomNormalized(double x, double y)
        {
            _settings.OverlayCustomX = Math.Clamp(x, 0, 1);
            _settings.OverlayCustomY = Math.Clamp(y, 0, 1);
            SettingsManager.Save(_settings);
            OnPropertyChanged(nameof(OverlayCustomX));
            OnPropertyChanged(nameof(OverlayCustomY));
            NotifyOverlayDerived();
        }

        // Bound by the corner segmented control: returns "-1" (no corner highlighted) while in free
        // placement, so the segs visibly deselect; setting it picks a corner and exits free placement.
        public string OverlayCornerSelection
        {
            get => OverlayUsesCustom ? "-1" : _settings.OverlayPosition.ToString();
            set { if (int.TryParse(value, out int p) && p is >= 0 and <= 3) OverlayPosition = p; }
        }

        // The free-placement X/Y as whole percents, for the numeric fields (only meaningful in custom mode).
        public int OverlayPosXPercent
        {
            get => OverlayUsesCustom ? (int)Math.Round(_settings.OverlayCustomX * 100) : 0;
            set => SetOverlayCustomNormalized(Math.Clamp(value, 0, 100) / 100.0,
                       _settings.OverlayCustomY >= 0 ? _settings.OverlayCustomY : 0);
        }
        public int OverlayPosYPercent
        {
            get => OverlayUsesCustom ? (int)Math.Round(_settings.OverlayCustomY * 100) : 0;
            set => SetOverlayCustomNormalized(_settings.OverlayCustomX >= 0 ? _settings.OverlayCustomX : 0,
                       Math.Clamp(value, 0, 100) / 100.0);
        }

        private void NotifyOverlayDerived()
        {
            OnPropertyChanged(nameof(OverlayUsesCustom));
            OnPropertyChanged(nameof(OverlayCornerSelection));
            OnPropertyChanged(nameof(OverlayPosXPercent));
            OnPropertyChanged(nameof(OverlayPosYPercent));
        }

        public int OverlayFontSize
        {
            get => _settings.OverlayFontSize;
            set { var v = value < 0 ? 0 : value; if (_settings.OverlayFontSize != v) { _settings.OverlayFontSize = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        public string OverlayFontFamily
        {
            get => _settings.OverlayFontFamily;
            set { if (_settings.OverlayFontFamily != value) { _settings.OverlayFontFamily = value ?? "Consolas"; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        // Colour/opacity for the overlay text and its backdrop box. Hex setters keep the last valid
        // value on bad input (ClampFlash's UpdateTarget snaps the box back on blur).
        public string OverlayTextColor
        {
            get => _settings.OverlayTextColor;
            set { if (TryNormalizeHex(value, out var v) && _settings.OverlayTextColor != v) { _settings.OverlayTextColor = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }
        public int OverlayTextOpacity
        {
            get => _settings.OverlayTextOpacity;
            set { var v = Math.Clamp(value, 0, 100); if (_settings.OverlayTextOpacity != v) { _settings.OverlayTextOpacity = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }
        public string OverlayBackColor
        {
            get => _settings.OverlayBackColor;
            set { if (TryNormalizeHex(value, out var v) && _settings.OverlayBackColor != v) { _settings.OverlayBackColor = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }
        public int OverlayBackOpacity
        {
            get => _settings.OverlayBackOpacity;
            set { var v = Math.Clamp(value, 0, 100); if (_settings.OverlayBackOpacity != v) { _settings.OverlayBackOpacity = v; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }

        private static bool TryNormalizeHex(string? input, out string hex)
        {
            hex = "";
            if (string.IsNullOrWhiteSpace(input)) return false;
            string s = input.Trim().TrimStart('#');
            // 6 digits only: alpha lives in the opacity fields, and the renderer's parser
            // (ColorTranslator.FromHtml) rejects #AARRGGBB — accepting it here would let a value
            // "take" in the box while silently not applying to the frames.
            if (s.Length != 6) return false;
            foreach (char c in s)
                if (!Uri.IsHexDigit(c)) return false;
            hex = "#" + s.ToUpperInvariant();
            return true;
        }

        private OverlayConfig BuildOverlay() => new()
        {
            Enabled = _settings.OverlayTimestamp,
            Text = _settings.OverlayText,
            Position = _settings.OverlayPosition,
            FontSize = _settings.OverlayFontSize,
            FontFamily = _settings.OverlayFontFamily,
            CustomX = _settings.OverlayCustomX,
            CustomY = _settings.OverlayCustomY,
            TextColor = _settings.OverlayTextColor,
            TextOpacity = _settings.OverlayTextOpacity,
            BackColor = _settings.OverlayBackColor,
            BackOpacity = _settings.OverlayBackOpacity,
        };

        public bool OpenFolderAfterEncode
        {
            get => _settings.OpenFolderAfterEncode;
            set { if (_settings.OpenFolderAfterEncode != value) { _settings.OpenFolderAfterEncode = value; SettingsManager.Save(_settings); OnPropertyChanged(); } }
        }
    }
}
