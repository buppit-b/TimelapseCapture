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
using TimelapseCapture; // Core: settings, sessions, ffmpeg, capture engine, screen helper

namespace TimelapseCapture.Wpf.ViewModels
{
    /// <summary>
    /// MainViewModel — the target model: h/m/s value, Video-length vs recording-Timer kind,
    /// commit/normalize, and the pause-aware run clock they share.
    /// </summary>
    public partial class MainViewModel
    {
        // The target value, in seconds. TargetKind decides what it MEANS: a video length to aim for
        // (frames goal, the original behaviour) or a recording timer (stop after this much active
        // capture — paused time doesn't count, which is what makes pause useful mid-run).
        private int _targetSeconds = 30;
        private double _timerRunBase;   // _accumulatedSeconds when this run started — the rec-timer datum

        private int _targetKind;   // 0 = video length · 1 = recording timer. Transient, like the value.
        public int TargetKind
        {
            get => _targetKind;
            set
            {
                if (SetProperty(ref _targetKind, value))
                {
                    OnPropertyChanged(nameof(StopAtTargetVisible));
                    RefreshTargetHint();
                    UpdateCaptureToTarget();
                    RefreshStats();
                    BumpRecalc();
                    TargetPulse++;
                }
            }
        }

        /// <summary>The "Stop at target" checkbox only applies to the video-length kind — a timer always stops.</summary>
        public bool StopAtTargetVisible => _targetKind == 0;

        // Three wheel-friendly boxes (h / m / s). Each setter recomputes the total from its component,
        // so overflow normalizes on commit: typing 90 into minutes reads back as 1h 30m.
        public int TargetHours
        {
            get => _targetSeconds / 3600;
            set => CommitTarget((long)Math.Max(0, value) * 3600 + _targetSeconds % 3600);
        }
        public int TargetMinutes
        {
            get => _targetSeconds % 3600 / 60;
            set => CommitTarget((long)(_targetSeconds / 3600) * 3600 + (long)Math.Max(0, value) * 60 + _targetSeconds % 60);
        }
        public int TargetSecondsBox
        {
            get => _targetSeconds % 60;
            set => CommitTarget((long)(_targetSeconds / 60) * 60 + Math.Max(0, value));
        }

        private string _targetHint = "";
        public string TargetHint { get => _targetHint; set => SetProperty(ref _targetHint, value); }

        private bool _targetHintError;
        public bool TargetHintError { get => _targetHintError; set => SetProperty(ref _targetHintError, value); }

        private void CommitTarget(long totalSeconds)
        {
            if (totalSeconds < 1)
            {
                TargetHint = "target must be at least 1 second";
                TargetHintError = true;
                NotifyTargetBoxes();   // snap the boxes back to the kept value
                return;
            }
            int clamped = (int)Math.Min(totalSeconds, 360000);   // 100h cap — keeps the frames math in int range
            bool changed = clamped != _targetSeconds;
            _targetSeconds = clamped;
            NotifyTargetBoxes();
            RefreshTargetHint();
            if (changed)
            {
                UpdateCaptureToTarget();
                RefreshStats();     // projection / progress reflect the new target
                BumpRecalc();       // flash the affected stats
                TargetPulse++;      // pulse the field outline + "Target" label to confirm the commit
            }
        }

        private void NotifyTargetBoxes()
        {
            OnPropertyChanged(nameof(TargetHours));
            OnPropertyChanged(nameof(TargetMinutes));
            OnPropertyChanged(nameof(TargetSecondsBox));
        }

        private void RefreshTargetHint()
        {
            TargetHintError = false;
            TargetHint = _targetKind == 1
                ? $"= record for {HumanDuration(_targetSeconds)}, then stop"
                : $"= a {HumanDuration(_targetSeconds)} video";
        }

        /// <summary>Clears every "don't ask again" choice; returns how many confirmations came back.</summary>
        public int ResetSuppressedPrompts()
        {
            int n = _settings.SuppressedPrompts?.Count ?? 0;
            if (n > 0)
            {
                _settings.SuppressedPrompts!.Clear();
                SettingsManager.Save(_settings);
            }
            return n;
        }

        // The target isn't per-session state — reset on session switches / restore-defaults.
        private void ResetTarget()
        {
            _targetSeconds = 30;
            _targetKind = 0;
            OnPropertyChanged(nameof(TargetKind));
            OnPropertyChanged(nameof(StopAtTargetVisible));
            NotifyTargetBoxes();
            RefreshTargetHint();
        }

        /// <summary>Seconds of ACTIVE recording in the current run (pause excluded) — the rec-timer's clock.</summary>
        private double RunActiveSeconds()
        {
            double current = (IsCapturing && _captureStart.HasValue) ? (DateTime.Now - _captureStart.Value).TotalSeconds : 0;
            return Math.Max(0, _accumulatedSeconds + current - _timerRunBase);
        }

    }
}
