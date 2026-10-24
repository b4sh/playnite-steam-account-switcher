using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Playnite.SDK;
using SteamAccountSwitcher.Core.Decision;
using SteamAccountSwitcher.Core.Steam;

namespace SteamAccountSwitcher
{
    /// <summary>
    /// "Ask before start" window (SPEC §7.5): one button per account plus Cancel, usable with arrows/Enter/Esc
    /// (and a controller in Fullscreen mode). Must be called on the UI thread.
    /// </summary>
    internal static class AccountPicker
    {
        /// <summary>The chosen account, or null when the user cancelled.</summary>
        public static SteamAccount Show(IPlayniteAPI api, string gameName, IReadOnlyList<SteamAccount> accounts, SteamState steam)
        {
            var window = api.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowCloseButton = true,
                ShowMaximizeButton = false,
                ShowMinimizeButton = false,
            });
            window.Owner = api.Dialogs.GetCurrentAppWindow();
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.SizeToContent = SizeToContent.WidthAndHeight;
            window.ResizeMode = ResizeMode.NoResize;
            window.Title = ResourceProvider.GetString("LOCSAS_PluginName");

            var panel = new StackPanel { Margin = new Thickness(20), MinWidth = 320 };
            KeyboardNavigation.SetDirectionalNavigation(panel, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Cycle);

            var header = new TextBlock
            {
                Text = string.Format(ResourceProvider.GetString("LOCSAS_PickerHeader"), gameName),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 480,
                Margin = new Thickness(0, 0, 0, 12),
            };
            header.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            panel.Children.Add(header);

            SteamAccount chosen = null;
            var buttons = new List<Button>();
            foreach (var account in accounts)
            {
                var label = account.DisplayName;
                if (steam != null && steam.IsRunning && steam.ActiveUser == account.AccountId)
                {
                    label += " – " + ResourceProvider.GetString("LOCSAS_PickerLoggedIn");
                }

                if (!account.RememberPassword)
                {
                    label += " – " + ResourceProvider.GetString("LOCSAS_NoRememberedPasswordShort");
                }

                // TextBlock content: a plain string would treat "_" in logins as an access-key marker.
                var button = new Button
                {
                    Content = new TextBlock { Text = label },
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 6),
                };
                var current = account;
                button.Click += (s, e) =>
                {
                    chosen = current;
                    window.DialogResult = true;
                };
                buttons.Add(button);
                panel.Children.Add(button);
            }

            var cancel = new Button
            {
                Content = new TextBlock { Text = ResourceProvider.GetString("LOCSAS_ButtonCancelStart") },
                IsCancel = true,
                HorizontalAlignment = HorizontalAlignment.Right,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 12, 0, 0),
            };
            panel.Children.Add(cancel);

            window.Content = panel;
            var preferred = LaunchTarget.PreferredIndex(accounts, steam);
            window.Loaded += (s, e) => (preferred >= 0 ? buttons[preferred] : cancel).Focus();

            return window.ShowDialog() == true ? chosen : null;
        }
    }
}
