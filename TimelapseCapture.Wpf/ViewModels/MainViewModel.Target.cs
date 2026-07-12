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
        // What a target MEANS, chosen by TargetKind:
        public const int TargetVideo = 0;   // aim for a video of _targetSeconds length (frames goal)
        public const int TargetTimer = 1;   // stop after _targetSeconds of ACTIVE capture (pause excluded)
        public const int TargetSize = 2;    // stop when the session's frames reach _targetSizeMB on disk

        // The target value, in seconds (video / timer). Transient — not per-session state.
        private int _targetSeconds = 30;
        private int _targetSizeMB = 5120;   // Size target: 5 GB default
        private double _timerRunBase;       // TotalActive when THIS run started — the per-run Run clock datum
        private double _timerGoalBase;      // TotalActive when the timer was (re)armed — the goal datum;
                                            // survives stop/start so the timer accumulates toward its goal

        private int _targetKind;
        public int TargetKind
        {
            get => _targetKind;
            set
            {
                if (SetProperty(ref _targetKind, value))
                {
                    if (value == TargetTimer) _timerGoalBase = TotalActiveSeconds();   // arm fresh from now
                    OnPropertyChanged(nameof(StopAtTargetVisible));
                    OnPropertyChanged(nameof(ShowTargetDuration));
                    OnPropertyChanged(nameof(ShowTargetSize));
                    OnPropertyChanged(nameof(TimerResetVisible));
                    RefreshTargetHint();
                    UpdateCaptureToTarget();
                    RefreshStats();
                    BumpRecalc();
                    TargetPulse++;
                }
            }
        }

        /// <summary>"Stop at target" checkbox applies only to the video-length kind — timer and size always stop.</summary>
        public bool StopAtTargetVisible => _targetKind == TargetVideo;
        /// <summary>The h/m/s boxes show for video/timer; the GB box shows for size.</summary>
        public bool ShowTargetDuration => _targetKind != TargetSize;
        public bool ShowTargetSize => _targetKind == TargetSize;
        /// <summary>The Reset button shows only for the recording timer (the one goal that accumulates).</summary>
        public bool TimerResetVisible => _targetKind == TargetTimer;

        // Size-target budget, edited in GB (stored as MB). 0.1 GB .. 4096 GB.
        public double TargetSizeGB
        {
            get => Math.Round(_targetSizeMB / 1024.0, 2);
            set
            {
                int mb = (int)Math.Clamp(Math.Round(value * 1024.0), 100, 4096L * 1024);
                if (mb == _targetSizeMB) return;
                _targetSizeMB = mb;
                OnPropertyChanged();
                RefreshTargetHint();
                UpdateCaptureToTarget();
                RefreshStats();
                BumpRecalc();
                TargetPulse++;
            }
        }

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
            TargetHint = _targetKind switch
            {
                TargetTimer => $"= record for {HumanDurationPrecise(_targetSeconds)}, then stop",
                TargetSize => $"= capture up to {FormatBudget(_targetSizeMB)}, then stop",
                _ => $"= a {HumanDuration(_targetSeconds)} video",
            };
        }

        // "5 GB" / "512 MB" for the size budget.
        private static string FormatBudget(double mb) => mb >= 1024 ? $"{mb / 1024.0:0.##} GB" : $"{mb:0} MB";

        /// <summary>Re-arm the recording timer from now, discarding accumulated progress toward the goal.</summary>
        private void ResetTimer()
        {
            _timerGoalBase = TotalActiveSeconds();
            UpdateCaptureToTarget();
            RefreshStats();
            BumpRecalc();
            TargetPulse++;
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
            _targetSizeMB = 5120;
            _targetKind = TargetVideo;
            _timerGoalBase = 0;
            OnPropertyChanged(nameof(TargetKind));
            OnPropertyChanged(nameof(StopAtTargetVisible));
            OnPropertyChanged(nameof(ShowTargetDuration));
            OnPropertyChanged(nameof(ShowTargetSize));
            OnPropertyChanged(nameof(TimerResetVisible));
            OnPropertyChanged(nameof(TargetSizeGB));
            NotifyTargetBoxes();
            RefreshTargetHint();
        }

        /// <summary>Total ACTIVE capture time across the session's runs (pause excluded). The base for both
        /// the per-run Run clock and the timer goal.</summary>
        private double TotalActiveSeconds()
        {
            double current = (IsCapturing && _captureStart.HasValue) ? (DateTime.Now - _captureStart.Value).TotalSeconds : 0;
            return _accumulatedSeconds + current;
        }

        /// <summary>Active time in the CURRENT run (resets each start) — drives the Run clock.</summary>
        private double RunActiveSeconds() => Math.Max(0, TotalActiveSeconds() - _timerRunBase);

        /// <summary>Active time toward the recording-timer GOAL — accumulates across stops (resets only on
        /// the Reset button / new session), which is what lets Stop be non-destructive to the timer.</summary>
        private double TimerProgressSeconds() => Math.Max(0, TotalActiveSeconds() - _timerGoalBase);

        /// <summary>Current session size on disk (MB) using the last sampled average frame size.</summary>
        private double SessionSizeMB() => _lastAvgFrameKb * _frameCount / 1024.0;

    }
}
