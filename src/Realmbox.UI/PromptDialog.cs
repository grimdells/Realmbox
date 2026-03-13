using System.Windows;
using System.Windows.Controls;

namespace Realmbox.UI
{
    /// <summary>
    /// Simple single-field input dialog used by menu item handlers.
    /// Returns the entered string, or null if the user cancelled.
    /// </summary>
    internal static class PromptDialog
    {
        public static string? Show(string title, string label, string current, Window owner)
        {
            Window dialog = new()
            {
                Title           = title,
                Width           = 420,
                Height          = 150,
                ResizeMode      = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner           = owner,
            };

            StackPanel panel = new() { Margin = new Thickness(12) };

            panel.Children.Add(new TextBlock
            {
                Text       = label,
                Margin     = new Thickness(0, 0, 0, 6),
                TextWrapping = System.Windows.TextWrapping.Wrap,
            });

            TextBox input = new()
            {
                Text           = current,
                Margin         = new Thickness(0, 0, 0, 10),
                Padding        = new Thickness(4),
            };
            panel.Children.Add(input);

            StackPanel buttons = new()
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };

            bool confirmed = false;

            Button ok = new()
            {
                Content = "Save",
                Width   = 80,
                Margin  = new Thickness(0, 0, 8, 0),
                IsDefault = true,
            };
            ok.Click += (_, _) => { confirmed = true; dialog.Close(); };

            Button cancel = new()
            {
                Content   = "Cancel",
                Width     = 80,
                IsCancel  = true,
            };
            cancel.Click += (_, _) => dialog.Close();

            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            dialog.Content = panel;
            dialog.ShowDialog();

            return confirmed ? input.Text : null;
        }
    }
}
