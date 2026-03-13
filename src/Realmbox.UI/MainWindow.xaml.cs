using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Realmbox.Core.Settings;
using Realmbox.Core.Util;
using Realmbox.UI.Controls;
using Ookii.Dialogs.Wpf;

namespace Realmbox.UI
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly SettingsManager<UserSettings> _settingsManager;
        private UserSettings? _settings;
        private ObservableCollection<Account> _accounts;
        private Account? _selectedAccount;
        private string _exaltPath = string.Empty;
        private string _deviceToken = string.Empty;
        private string _clientPath = string.Empty;
        private int _groupLaunchDelay = 10;
        private bool _isLaunchingGroup = false;
        private CancellationTokenSource? _groupLaunchCts;
        private Point _dragStartPoint;
        private RealmboxTray? _tray;

        public event PropertyChangedEventHandler? PropertyChanged;

        public Snackbar Snackbar => SnackbarElement;

        public ObservableCollection<Account> Accounts
        {
            get => _accounts;
            set { _accounts = value; OnPropertyChanged(); }
        }

        public Account? SelectedAccount
        {
            get => _selectedAccount;
            set { _selectedAccount = value; OnPropertyChanged(); }
        }

        public UserSettings Settings
        {
            get => _settings ?? throw new InvalidOperationException("Settings not initialized");
            private set { _settings = value; OnPropertyChanged(); }
        }

        public string ExaltPath
        {
            get => _exaltPath;
            set { _exaltPath = value; OnPropertyChanged(); }
        }

        public string DeviceToken
        {
            get => _deviceToken;
            set { _deviceToken = value; OnPropertyChanged(); }
        }

        public string ClientPath
        {
            get => _clientPath;
            set { _clientPath = value; OnPropertyChanged(); }
        }

        public int GroupLaunchDelay
        {
            get => _groupLaunchDelay;
            set
            {
                if (_groupLaunchDelay == value)
                    return;
                _groupLaunchDelay = value;
                OnPropertyChanged();

                // persist immediately when the delay changes
                if (_settings != null)
                {
                    Settings.GroupLaunchDelay = value;
                    try
                    {
                        _settingsManager.SaveSettings(Settings);
                    }
                    catch (Exception ex)
                    {
                        // show but don't crash; settings saving failures are non-fatal
                        Snackbar.Show($"Failed to save settings: {ex.Message}");
                    }
                }
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            _settingsManager = new SettingsManager<UserSettings>("settings.json");
            _accounts = [];
            DataContext = this;
            LoadSettings();

            _tray = new RealmboxTray(this);
            Closing += (_, _) => _tray?.Dispose();
        }

        private void LoadSettings()
        {
            try
            {
                Settings = _settingsManager.LoadSettings() ?? new UserSettings();
                Settings.Accounts ??= [];
                ApplySettings(Settings);
            }
            catch (Exception ex)
            {
                Snackbar.Show($"Failed to load settings: {ex.Message}");
            }
        }

        private void BtnAddAccount_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                EditAccountDialog dialog = new(null);

                if (dialog.ShowDialog() == true)
                {
                    if (Settings.Accounts!.Any(x => x.Name == dialog.AccountName))
                    {
                        Snackbar.Show("An account with this name already exists");
                        return;
                    }

                    Account account = new()
                    {
                        Name = dialog.AccountName,
                        Base64EMail = Helper.Base64Encode(dialog.Email),
                        Base64Password = Helper.Base64Encode(dialog.Password),
                        Group = dialog.Group,
                        ProxyHost = dialog.ProxyHost,
                        ProxyPort = dialog.ProxyPort,
                        ProxyUsername = dialog.ProxyUsername,
                        ProxyPassword = dialog.ProxyPassword,
                    };

                    Settings.Accounts!.Add(account);
                    _settingsManager.SaveSettings(Settings);
                    ApplySettings(Settings);
                    Snackbar.Show("Account added successfully");
                }
            }
            catch (Exception ex)
            {
                Snackbar.Show($"Failed to add account: {ex.Message}");
            }
        }

        private void BtnEditAccount_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (((FrameworkElement)sender).DataContext is not Account account) return;

                EditAccountDialog dialog = new(account);
                if (dialog.ShowDialog() == true)
                {
                    account.Name = dialog.AccountName;
                    account.Base64EMail = Helper.Base64Encode(dialog.Email);
                    account.Base64Password = Helper.Base64Encode(dialog.Password);
                    account.Group = dialog.Group;
                    account.ProxyHost = dialog.ProxyHost;
                    account.ProxyPort = dialog.ProxyPort;
                    account.ProxyUsername = dialog.ProxyUsername;
                    account.ProxyPassword = dialog.ProxyPassword;

                    _settingsManager.SaveSettings(Settings);
                    ApplySettings(Settings);
                    Snackbar.Show("Account edited successfully");
                }
            }
            catch (Exception ex)
            {
                Snackbar.Show($"Failed to edit account: {ex.Message}");
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SelectedAccount is null)
                {
                    Snackbar.Show("Please select an account to delete");
                    return;
                }

                if (MessageBox.Show($"Are you sure you want to delete account '{SelectedAccount.Name}'?",
                    "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    Settings.Accounts!.Remove(SelectedAccount);
                    _settingsManager.SaveSettings(Settings);
                    ApplySettings(Settings);
                    Snackbar.Show("Account deleted successfully");
                }
            }
            catch (Exception ex)
            {
                Snackbar.Show($"Failed to delete account: {ex.Message}");
            }
        }

        private void ApplySettings(UserSettings userSettings)
        {
            ExaltPath = userSettings?.ExaltPath ?? "";
            ClientPath = userSettings?.ClientPath ?? "";
            DeviceToken = userSettings?.DeviceToken ?? "";
            GroupLaunchDelay = userSettings?.GroupLaunchDelay ?? 10;
            Accounts = [.. userSettings?.Accounts ?? []];
            // set GroupLaunchDelay property which will also update UI via binding
            GroupLaunchDelay = userSettings?.GroupLaunchDelay ?? 10;
            RefreshGroupCombo();
        }

        private void RefreshGroupCombo()
        {
            string? selected = CmbGroups.SelectedItem as string;
            var groups = Accounts
                .Where(a => !string.IsNullOrWhiteSpace(a.Group))
                .Select(a => a.Group!)
                .Distinct()
                .OrderBy(g => g)
                .ToList();

            CmbGroups.ItemsSource = groups;

            if (selected != null && groups.Contains(selected))
                CmbGroups.SelectedItem = selected;
            else if (groups.Count > 0)
                CmbGroups.SelectedIndex = 0;

            // UI binding will already update the textbox; no need to set Text explicitly
        }

        private async void BtnOpenSelectedItem_Click(object sender, RoutedEventArgs e)
        {
            // Resolve the account from the button's DataContext (row), not SelectedAccount
            Account? account = null;
            if (((FrameworkElement)sender).DataContext is Account rowAccount)
            {
                account = rowAccount;
            }
            else
            {
                account = SelectedAccount;
            }

            if (account is null)
            {
                Snackbar.Show("Please select an account");
                return;
            }

            if (string.IsNullOrEmpty(Settings.ExaltPath))
            {
                Snackbar.Show("Please set the Exalt path in settings");
                return;
            }

            try
            {
                string proxyInfo = account.HasProxy ? $" via proxy {account.ProxyHost}:{account.ProxyPort}" : "";
                Snackbar.Show($"Launching {account.Name}{proxyInfo}...");

                await Helper.LaunchExaltClient(
                    Settings.ExaltPath!,
                    Helper.Base64Decode(account.Base64EMail),
                    Helper.Base64Decode(account.Base64Password),
                    Settings.DeviceToken ?? "",
                    account
                );

                TriggerAutoInjectIfEnabled();
            }
            catch (Exception ex)
            {
                Snackbar.Show($"Failed to launch: {ex.Message}");
            }
        }

        private async void BtnLaunchGroup_Click(object sender, RoutedEventArgs e)
        {
            // If already launching, cancel and let the finally block clean up
            if (_isLaunchingGroup)
            {
                _groupLaunchCts?.Cancel();
                BtnLaunchGroup.IsEnabled = false;
                return;
            }

            string? group = CmbGroups.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(group))
            {
                Snackbar.Show("No group selected");
                return;
            }

            if (string.IsNullOrEmpty(Settings.ExaltPath))
            {
                Snackbar.Show("Please set the Exalt path in settings");
                return;
            }

            if (!int.TryParse(TxtGroupDelay.Text, out int delay) || delay < 0)
            {
                Snackbar.Show("Invalid delay - must be a number >= 0");
                return;
            }

            List<Account> groupAccounts = Accounts
                .Where(a => a.Group == group)
                .ToList();

            if (groupAccounts.Count == 0)
            {
                Snackbar.Show($"No accounts in group '{group}'");
                return;
            }

            _isLaunchingGroup = true;
            BtnLaunchGroup.Content = "Cancel";
            _groupLaunchCts = new CancellationTokenSource();
            CancellationToken ct = _groupLaunchCts.Token;

            try
            {
                for (int i = 0; i < groupAccounts.Count; i++)
                {
                    if (ct.IsCancellationRequested) break;

                    Account account = groupAccounts[i];
                    TxtGroupLaunchStatus.Text = $"Launching {i + 1}/{groupAccounts.Count}: {account.Name}...";

                    try
                    {
                        await Helper.LaunchExaltClient(
                            Settings.ExaltPath!,
                            Helper.Base64Decode(account.Base64EMail),
                            Helper.Base64Decode(account.Base64Password),
                            Settings.DeviceToken ?? "",
                            account
                        );
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Snackbar.Show($"Failed to launch {account.Name}: {ex.Message}");
                    }

                    // Wait between launches (skip delay after last account)
                    if (i < groupAccounts.Count - 1 && delay > 0)
                    {
                        for (int s = delay; s > 0; s--)
                        {
                            if (ct.IsCancellationRequested) break;
                            TxtGroupLaunchStatus.Text = $"Launched {i + 1}/{groupAccounts.Count}: {account.Name} - next in {s}s...";
                            await Task.Delay(1000, ct);
                        }
                    }
                }

                if (!ct.IsCancellationRequested)
                {
                    TxtGroupLaunchStatus.Text = $"Done - launched {groupAccounts.Count} account(s).";

                    // Auto-inject once for the whole group - start injector now,
                    // keep it alive for the full auto-inject delay then close it.
                    if (Settings.AutoInject && !string.IsNullOrWhiteSpace(ClientPath))
                        TriggerGroupAutoInject();
                }
            }
            catch (OperationCanceledException)
            {
                TxtGroupLaunchStatus.Text = "Cancelled.";
            }
            finally
            {
                _isLaunchingGroup = false;
                BtnLaunchGroup.Content = "Launch Group";
                BtnLaunchGroup.IsEnabled = true;
                _groupLaunchCts?.Dispose();
                _groupLaunchCts = null;
            }
        }

        private void MenuCloseAllClients_Click(object sender, RoutedEventArgs e)
        {
            int count = Helper.CloseAllClients();
            Snackbar.Show(count > 0
                ? $"Closed {count} Exalt client{(count == 1 ? "" : "s")}"
                : "No running Exalt clients found");
        }

        private void MenuOpenClient_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ClientPath))
            {
                Snackbar.Show("Injector path not set - use Settings > Injector Path first");
                return;
            }

            if (System.Diagnostics.Process.GetProcessesByName("RotMG Exalt").Length == 0)
            {
                Snackbar.Show("No Exalt client is running - launch a client first");
                return;
            }

            RunInjector();
        }

        private void RunInjector()
        {
            string[] required = ["ROTMGInjector.exe", "rotmg.dll"];

            foreach (string file in required)
            {
                if (!File.Exists(Path.Combine(ClientPath!, file)))
                {
                    Snackbar.Show($"{file} not found in: {ClientPath}");
                    return;
                }
            }

            string injectDir = AppContext.BaseDirectory;

            try
            {
                foreach (string file in required)
                    File.Copy(Path.Combine(ClientPath!, file),
                              Path.Combine(injectDir, file),
                              overwrite: true);
            }
            catch (Exception ex)
            {
                Snackbar.Show($"Failed to copy client files: {ex.Message}");
                return;
            }

            string exe = Path.Combine(injectDir, "ROTMGInjector.exe");

            try
            {
                var proc = System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });

                if (proc == null)
                {
                    Snackbar.Show("Failed to launch ROTMGInjector.exe");
                    return;
                }

                Snackbar.Show("Injecting...");

                // Kill injector after 1 second - injection is complete by then
                Task.Delay(1000).ContinueWith(_ =>
                {
                    try { if (!proc.HasExited) proc.Kill(); } catch { }
                });
            }
            catch (Exception ex)
            {
                Snackbar.Show($"Failed to launch injector: {ex.Message}");
            }
        }

        private void TriggerGroupAutoInject()
        {
            string[] required = ["ROTMGInjector.exe", "rotmg.dll"];
            foreach (string file in required)
                if (!File.Exists(Path.Combine(ClientPath!, file))) return;

            int delaySecs = Settings.AutoInjectDelay > 0 ? Settings.AutoInjectDelay : 10;
            string injectDir = AppContext.BaseDirectory;

            try
            {
                foreach (string file in required)
                    File.Copy(Path.Combine(ClientPath!, file),
                              Path.Combine(injectDir, file),
                              overwrite: true);
            }
            catch (Exception ex)
            {
                Snackbar.Show($"Failed to copy client files: {ex.Message}");
                return;
            }

            string exe = Path.Combine(injectDir, "ROTMGInjector.exe");

            try
            {
                var proc = System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });

                if (proc == null) { Snackbar.Show("Failed to launch injector"); return; }

                Snackbar.Show($"Injector running - closes in {delaySecs}s");

                // Keep injector alive for the full delay, then kill it once
                Task.Delay(delaySecs * 1000).ContinueWith(_ =>
                {
                    try { if (!proc.HasExited) proc.Kill(); } catch { }
                });
            }
            catch (Exception ex)
            {
                Snackbar.Show($"Failed to launch injector: {ex.Message}");
            }
        }

        private void TriggerAutoInjectIfEnabled()
        {
            if (!Settings.AutoInject) return;
            if (string.IsNullOrWhiteSpace(ClientPath)) return;

            string[] required = ["ROTMGInjector.exe", "rotmg.dll"];
            foreach (string file in required)
                if (!File.Exists(Path.Combine(ClientPath, file))) return;

            int delaySecs = Settings.AutoInjectDelay > 0 ? Settings.AutoInjectDelay : 10;
            Snackbar.Show($"Auto-inject in {delaySecs}s...");

            Task.Delay(delaySecs * 1000).ContinueWith(_ =>
                Dispatcher.Invoke(() =>
                {
                    if (System.Diagnostics.Process.GetProcessesByName("RotMG Exalt").Length == 0) return;
                    RunInjector();
                }));
        }

        private void MenuAdditionalSettings_Click(object sender, RoutedEventArgs e)
        {
            new AdditionalSettingsDialog(this, Settings, _settingsManager).ShowDialog();
        }

        private void MenuClientPath_Click(object sender, RoutedEventArgs e)
        {
            VistaFolderBrowserDialog dialog = new()
            {
                Description = "Select the client folder",
                UseDescriptionForTitle = true,
                SelectedPath = ClientPath
            };

            if (dialog.ShowDialog()!.Value)
            {
                if (!Directory.Exists(dialog.SelectedPath))
                {
                    Snackbar.Show("Selected path does not exist");
                    return;
                }
                ClientPath = dialog.SelectedPath;
                Settings.ClientPath = ClientPath;
                _settingsManager.SaveSettings(Settings);
                Snackbar.Show("Injector path saved");
            }
        }

        private void MenuExaltPath_Click(object sender, RoutedEventArgs e)
        {
            VistaFolderBrowserDialog dialog = new()
            {
                Description = "Select your Exalt installation folder",
                UseDescriptionForTitle = true,
                SelectedPath = ExaltPath
            };

            if (dialog.ShowDialog()!.Value)
            {
                if (!Directory.Exists(dialog.SelectedPath))
                {
                    Snackbar.Show("Selected path does not exist");
                    return;
                }
                ExaltPath = dialog.SelectedPath;
                Settings.ExaltPath = ExaltPath;
                _settingsManager.SaveSettings(Settings);
                Snackbar.Show("Exalt path saved");
            }
        }

        private void MenuDeviceToken_Click(object sender, RoutedEventArgs e)
        {
            string current = DeviceToken ?? "";
            string? result = PromptDialog.Show(
                "Device Token",
                "Enter your device token (leave blank to auto-generate):",
                current,
                this);

            if (result == null) return; // cancelled

            DeviceToken = result.Trim();
            Settings.DeviceToken = DeviceToken;
            _settingsManager.SaveSettings(Settings);
            Snackbar.Show("Device token saved");
        }

        private void TxtExaltPath_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // kept for compatibility - not used with menu bar
        }

        private bool ValidateSettings()
        {
            if (string.IsNullOrEmpty(ExaltPath))
            {
                Snackbar.Show("Exalt path not set - use Settings menu");
                return false;
            }

            if (!Directory.Exists(ExaltPath))
            {
                Snackbar.Show("Exalt path does not exist - use Settings menu to update it");
                return false;
            }

            return true;
        }

        private bool ValidateExaltLaunch()
        {
            if (SelectedAccount is null)
            {
                Snackbar.Show("Please select an account");
                return false;
            }

            if (string.IsNullOrEmpty(Settings.ExaltPath))
            {
                Snackbar.Show("Please set the Exalt path via the Settings menu");
                return false;
            }

            return true;
        }

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            PageAccounts.Visibility   = NavAccounts.IsChecked   == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void AccountsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void AccountsGrid_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;

            Vector diff = _dragStartPoint - e.GetPosition(null);
            if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            DataGridRow? row = FindVisualParent<DataGridRow>((DependencyObject)e.OriginalSource);
            if (row == null || row.Item is not Account draggedAccount) return;

            DataObject data = new("DraggedAccount", draggedAccount);
            DragDrop.DoDragDrop(row, data, DragDropEffects.Move);
        }

        private void AccountsGrid_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent("DraggedAccount")) return;

            DataGridRow? targetRow = FindVisualParent<DataGridRow>((DependencyObject)e.OriginalSource);
            if (targetRow == null || targetRow.Item is not Account targetAccount) return;

            if (e.Data.GetData("DraggedAccount") is not Account draggedAccount || draggedAccount == targetAccount) return;

            int indexDragged = Settings.Accounts!.IndexOf(draggedAccount);
            int indexTarget = Settings.Accounts.IndexOf(targetAccount);

            if (indexDragged < indexTarget)
            {
                Settings.Accounts.Insert(indexTarget + 1, draggedAccount);
                Settings.Accounts.RemoveAt(indexDragged);
            }
            else
            {
                Settings.Accounts.Insert(indexTarget, draggedAccount);
                Settings.Accounts.RemoveAt(indexDragged + 1);
            }

            _settingsManager.SaveSettings(Settings);
            ApplySettings(Settings);
            Snackbar.Show("Accounts reordered successfully");
        }

        private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T correctlyTyped) return correctlyTyped;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
