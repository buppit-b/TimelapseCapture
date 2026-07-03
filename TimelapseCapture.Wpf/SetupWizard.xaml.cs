using System.Windows;
using System.Windows.Controls;
using TimelapseCapture.Wpf.ViewModels;

namespace TimelapseCapture.Wpf
{
    /// <summary>
    /// First-run guided setup: output folder → what to capture → speed → go. A thin flow shell over the
    /// same MainViewModel commands the main window uses (nothing is duplicated), so anything set here is
    /// exactly what the app runs with. Re-runnable from Settings.
    /// </summary>
    public partial class SetupWizard : Window
    {
        private static readonly string[] Titles =
        {
            "WELCOME",
            "STEP 1 — WHERE TO SAVE",
            "STEP 2 — WHAT TO CAPTURE",
            "STEP 3 — CAPTURE SPEED",
            "STEP 4 — VIDEO ENCODER (FFMPEG)",
            "ALL SET",
        };

        private int _step;

        public SetupWizard()
        {
            InitializeComponent();
            Refresh();
        }

        private MainViewModel? Vm => DataContext as MainViewModel;
        private StackPanel[] Steps => new[] { step0, step1, step2, step3, step4, step5 };

        private void Refresh()
        {
            var steps = Steps;
            for (int i = 0; i < steps.Length; i++)
                steps[i].Visibility = i == _step ? Visibility.Visible : Visibility.Collapsed;

            stepTitle.Text = Titles[_step];
            backBtn.Visibility = _step == 0 ? Visibility.Hidden : Visibility.Visible;
            nextBtn.Content = _step == steps.Length - 1 ? "Finish" : "Next  ▶";

            string dots = "";
            for (int i = 0; i < steps.Length; i++) dots += i == _step ? "●" : "○";
            stepDots.Text = dots;
        }

        private void OnBack(object sender, RoutedEventArgs e)
        {
            if (_step > 0) { _step--; Refresh(); }
        }

        private void OnNext(object sender, RoutedEventArgs e)
        {
            if (Vm is not { } vm) { Close(); return; }

            // Validate on Next (friendlier than a mysteriously disabled button).
            if (_step == 1 && !vm.NewSessionCommand.CanExecute(null))
            {
                MessageBox.Show("Choose an output folder first — everything the app records lives there.",
                    "Setup", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (_step == 2 && !vm.StartCommand.CanExecute(null))
            {
                MessageBox.Show("Pick what to capture first — Full Screen is a fine default.",
                    "Setup", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_step == Steps.Length - 1)
            {
                vm.SimpleMode = simpleModeBox.IsChecked == true;
                DialogResult = true;
                return;
            }
            _step++;
            Refresh();
        }

        // A session is required before a region can be applied; create one quietly if there isn't one yet
        // (default name — no prompt mid-wizard; it can be renamed later by clicking the header name).
        private void EnsureSession() => Vm?.EnsureDefaultSession();

        private void OnPickFullScreen(object sender, RoutedEventArgs e)
        {
            EnsureSession();
            if (Vm is { } vm && vm.FullScreenCommand.CanExecute(null)) vm.FullScreenCommand.Execute(null);
        }

        private void OnPickRegion(object sender, RoutedEventArgs e)
        {
            EnsureSession();
            if (Vm is { } vm && vm.SelectRegionCommand.CanExecute(null)) vm.SelectRegionCommand.Execute(null);
        }

        private void OnPickWindow(object sender, RoutedEventArgs e)
        {
            EnsureSession();
            if (Vm is { } vm && vm.TrackWindowCommand.CanExecute(null)) vm.TrackWindowCommand.Execute(null);
        }
    }
}
