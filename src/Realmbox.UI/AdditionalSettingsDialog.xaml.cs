using System.Windows;
using Realmbox.Core.Settings;
using Realmbox.Core.Util;

namespace Realmbox.UI
{
    public partial class AdditionalSettingsDialog : Window
    {
        private readonly SettingsManager<UserSettings> _settingsManager;
        private readonly UserSettings _settings;

        public AdditionalSettingsDialog(Window owner, UserSettings settings, SettingsManager<UserSettings> settingsManager)
        {
            InitializeComponent();
            Owner            = owner;
            _settings        = settings;
            _settingsManager = settingsManager;

            // Load current values
            ChkAutoInject.IsChecked    = settings.AutoInject;
            TxtAutoInjectDelay.Text    = settings.AutoInjectDelay.ToString();
            UpdateDelayEnabled();
        }

        private void ChkAutoInject_Changed(object sender, RoutedEventArgs e) => UpdateDelayEnabled();

        private void UpdateDelayEnabled() =>
            TxtAutoInjectDelay.IsEnabled = ChkAutoInject.IsChecked == true;

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtAutoInjectDelay.Text, out int delay) || delay < 1)
            {
                MessageBox.Show("Inject delay must be a number >= 1.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _settings.AutoInject      = ChkAutoInject.IsChecked == true;
            _settings.AutoInjectDelay = delay;
            _settingsManager.SaveSettings(_settings);

            MessageBox.Show("Settings saved.", "Realmbox", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}
