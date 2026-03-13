using System.Windows;
using System.Windows.Threading;
using Realmbox.Core.Exceptions;
using Realmbox.Core.Util;
using Realmbox.UI.Controls;

namespace Realmbox.UI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnExit(ExitEventArgs e)
        {
            // Clean up any running SOCKS5 tunnels on app close
            Helper.StopAllTunnels();
            base.OnExit(e);
        }

        private Snackbar? GetSnackbar()
        {
            if (MainWindow is MainWindow mainWindow)
                return mainWindow.Snackbar;
            return null;
        }

        private void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            string message = e.Exception switch
            {
                ExaltPathNotFoundException             => "Make sure to enter exalt path in Settings",
                AccessTokenParseFailedException        => "Failed to parse access token, you might be rate limited, try later again",
                AccessTokenRetrievalFailedException    => "Failed to retrieve access token, try later again. Make sure credentials of the account are valid",
                ExaltExeNotFoundException              => "Couldn't start exalt. Make sure the path is correct and it contains the RotMG Exalt.exe file",
                _                                     => $"Something went wrong: {e.Exception.Message}"
            };

            Snackbar? snackbar = GetSnackbar();
            if (snackbar != null)
                snackbar.Show(message);
            else
                MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Warning);

            e.Handled = true;
        }
    }
}
