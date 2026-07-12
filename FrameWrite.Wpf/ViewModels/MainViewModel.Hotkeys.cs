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
    /// MainViewModel — the global-hotkey keymap: per-action bindings, conflict checks, registration
    /// reporting from the window, and the hotkey-invoked actions.
    /// </summary>
    public partial class MainViewModel
    {

        // ---- Global hotkeys (off by default; every action rebindable in the Settings keymap). ----
        public event Action? HotkeysChanged;

        public const string HotkeyStartStop = "startstop";
        public const string HotkeyPause = "pause";
        public const string HotkeyRegionSelect = "regionselect";
        /// <summary>Registration order — the window derives its Win32 hotkey ids from these indexes.</summary>
        public static readonly string[] HotkeyActions = { HotkeyStartStop, HotkeyPause, HotkeyRegionSelect };

        public static string HotkeyFriendly(string action) => action switch
        {
            HotkeyStartStop => "Start / Stop",
            HotkeyPause => "Pause / Resume",
            HotkeyRegionSelect => "Select region",
            _ => action,
        };

        public bool HotkeysEnabled
        {
            get => _settings.HotkeysEnabled;
            set { if (_settings.HotkeysEnabled != value) { _settings.HotkeysEnabled = value; SettingsManager.Save(_settings); OnPropertyChanged(); NotifyHotkeysChanged(); } }
        }

        // Older settings files carry only the single start/stop pair — seed the keymap from it once,
        // and make sure every known action has a row (future actions slot in the same way).
        private List<HotkeyBinding> EnsureHotkeys()
        {
            _settings.Hotkeys ??= new List<HotkeyBinding>
            {
                new() { Action = HotkeyStartStop, Modifiers = _settings.HotkeyModifiers, Vk = _settings.HotkeyVk },
            };
            foreach (var action in HotkeyActions)
                if (!_settings.Hotkeys.Any(b => b.Action == action))
                    _settings.Hotkeys.Add(new HotkeyBinding { Action = action });
            return _settings.Hotkeys;
        }

        public HotkeyBinding GetHotkey(string action) => EnsureHotkeys().First(b => b.Action == action);

        public string GetHotkeyDisplay(string action)
        {
            var b = GetHotkey(action);
            if (b.Vk == 0) return "not set";
            var parts = new List<string>();
            if ((b.Modifiers & 0x0002) != 0) parts.Add("Ctrl");
            if ((b.Modifiers & 0x0004) != 0) parts.Add("Shift");
            if ((b.Modifiers & 0x0001) != 0) parts.Add("Alt");
            if ((b.Modifiers & 0x0008) != 0) parts.Add("Win");
            var key = KeyInterop.KeyFromVirtualKey(b.Vk);
            parts.Add(key == Key.None ? $"VK 0x{b.Vk:X2}" : key.ToString());   // corrupt/foreign vk — stay honest
            return string.Join(" + ", parts);
        }

        // Registration happens in the window (it owns the HWND); it reports back here so Settings can
        // show WHY a binding isn't working — RegisterHotKey fails silently when another app holds the
        // combination, which otherwise reads as "the hotkey is broken".
        private string _hotkeyWarning = "";
        public string HotkeyWarning { get => _hotkeyWarning; private set => SetProperty(ref _hotkeyWarning, value); }

        public void ReportHotkeyRegistration(IReadOnlyList<string> failedActions)
        {
            HotkeyWarning = failedActions.Count == 0
                ? ""
                : "Couldn't register " +
                  string.Join(", ", failedActions.Select(a => $"{HotkeyFriendly(a)} ({GetHotkeyDisplay(a)})")) +
                  " — the combination may be in use by another app. Pick a different one.";
            if (HotkeyWarning.Length > 0) Logger.Log("Wpf", $"Hotkey registration failed: {HotkeyWarning}");
        }

        /// <summary>Bind a combo to an action. Returns null on success, or the OTHER action already using it.</summary>
        public string? SetHotkey(string action, ModifierKeys mods, Key key)
        {
            uint fs = 0;
            if (mods.HasFlag(ModifierKeys.Alt)) fs |= 0x0001;
            if (mods.HasFlag(ModifierKeys.Control)) fs |= 0x0002;
            if (mods.HasFlag(ModifierKeys.Shift)) fs |= 0x0004;
            if (mods.HasFlag(ModifierKeys.Windows)) fs |= 0x0008;
            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk == 0) return null;   // unmappable key — ignore (and never "conflict" with unbound rows)

            var conflict = EnsureHotkeys().FirstOrDefault(b => b.Action != action && b.Vk == vk && b.Modifiers == (int)fs);
            if (conflict != null) return conflict.Action;

            var binding = GetHotkey(action);
            binding.Modifiers = (int)fs;
            binding.Vk = vk;
            PersistHotkeys();
            return null;
        }

        public void ClearHotkey(string action)
        {
            var binding = GetHotkey(action);
            if (binding.Vk == 0) return;
            binding.Vk = 0;
            binding.Modifiers = 0;
            PersistHotkeys();
        }

        private void PersistHotkeys()
        {
            // Mirror start/stop into the legacy pair so old exports / older builds keep working.
            var ss = GetHotkey(HotkeyStartStop);
            if (ss.Vk != 0)
            {
                _settings.HotkeyModifiers = ss.Modifiers;
                _settings.HotkeyVk = ss.Vk;
            }
            SettingsManager.Save(_settings);
            NotifyHotkeysChanged();
        }

        private void NotifyHotkeysChanged()
        {
            OnPropertyChanged(nameof(StartHotkeyTooltip));
            OnPropertyChanged(nameof(StopHotkeyTooltip));
            HotkeysChanged?.Invoke();
        }

        // The Start/Stop buttons advertise the live binding instead of a hard-coded combo.
        public string StartHotkeyTooltip => "Start capture" + HotkeyHintSuffix(HotkeyStartStop);
        public string StopHotkeyTooltip => "Stop capture" + HotkeyHintSuffix(HotkeyStartStop);

        private string HotkeyHintSuffix(string action)
        {
            if (!HotkeysEnabled) return "";
            var b = GetHotkey(action);
            return b.Vk == 0 ? "" : $" — global hotkey: {GetHotkeyDisplay(action)}";
        }

        /// <summary>Global-hotkey action: pause/resume, with the buttons' own gating (needs a running capture).</summary>
        public void PauseHotkey()
        {
            if (PauseResumeCommand.CanExecute(null)) PauseResumeCommand.Execute(null);
        }

        /// <summary>Global-hotkey action: pick a capture region — works while the app sits in the tray.</summary>
        public void RegionSelectHotkey()
        {
            if (SelectRegionCommand.CanExecute(null)) SelectRegionCommand.Execute(null);
        }
        /// <summary>Global-hotkey action: toggle capture, respecting the same gating as the buttons.</summary>
        public void ToggleCaptureHotkey()
        {
            if (IsCapturing)
            {
                if (StopCommand.CanExecute(null)) StopCommand.Execute(null);
            }
            else if (StartCommand.CanExecute(null))
            {
                StartCommand.Execute(null);
            }
        }
    }
}
