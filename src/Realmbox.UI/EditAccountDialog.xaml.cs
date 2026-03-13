using System.Windows;
using Realmbox.Core.Settings;
using Realmbox.Core.Util;

namespace Realmbox.UI
{
    /// <summary>
    /// Interaction logic for EditAccountDialog.xaml
    /// </summary>
    public partial class EditAccountDialog : Window
    {
        public string AccountName { get; private set; } = default!;
        public string Email { get; private set; } = default!;
        public string Password { get; private set; } = default!;
        public string? Group { get; private set; }

        // Proxy output properties
        public string? ProxyHost { get; private set; }
        public int? ProxyPort { get; private set; }
        public string? ProxyUsername { get; private set; }
        public string? ProxyPassword { get; private set; }

        public EditAccountDialog(Account? account)
        {
            InitializeComponent();

            if (account != null)
            {
                Title = "Edit Account";
                txtName.Text = account.Name;
                txtEmail.Text = Helper.Base64Decode(account.Base64EMail);
                txtPassword.Password = Helper.Base64Decode(account.Base64Password);

                // Populate proxy fields
                txtProxyHost.Text = account.ProxyHost ?? "";
                txtProxyPort.Text = account.ProxyPort?.ToString() ?? "";
                txtProxyUsername.Text = account.ProxyUsername ?? "";
                txtProxyPassword.Password = account.ProxyPassword ?? "";
                txtGroup.Text = account.Group ?? "";
            }
            else
            {
                Title = "Add Account";
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtName.Text) ||
                string.IsNullOrWhiteSpace(txtEmail.Text) ||
                string.IsNullOrWhiteSpace(txtPassword.Password))
            {
                MessageBox.Show("Name, Email, and Password are required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Validate proxy fields - both host and port must be set together
            string proxyHostRaw = txtProxyHost.Text.Trim();
            string proxyPortRaw = txtProxyPort.Text.Trim();

            if (!string.IsNullOrEmpty(proxyHostRaw) || !string.IsNullOrEmpty(proxyPortRaw))
            {
                if (string.IsNullOrEmpty(proxyHostRaw) || string.IsNullOrEmpty(proxyPortRaw))
                {
                    MessageBox.Show("Both proxy host and port must be provided together.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!int.TryParse(proxyPortRaw, out int parsedPort) || parsedPort < 1 || parsedPort > 65535)
                {
                    MessageBox.Show("Proxy port must be a number between 1 and 65535.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ProxyHost = proxyHostRaw;
                ProxyPort = parsedPort;
                ProxyUsername = string.IsNullOrWhiteSpace(txtProxyUsername.Text) ? null : txtProxyUsername.Text.Trim();
                ProxyPassword = string.IsNullOrWhiteSpace(txtProxyPassword.Password) ? null : txtProxyPassword.Password;
            }
            else
            {
                // No proxy
                ProxyHost = null;
                ProxyPort = null;
                ProxyUsername = null;
                ProxyPassword = null;
            }

            AccountName = txtName.Text;
            Email = txtEmail.Text;
            Password = txtPassword.Password;
            Group = string.IsNullOrWhiteSpace(txtGroup.Text) ? null : txtGroup.Text.Trim();

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
