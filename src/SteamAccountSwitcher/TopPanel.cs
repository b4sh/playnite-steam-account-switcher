using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Plugins;
using SteamAccountSwitcher.Core.Decision;
using SteamAccountSwitcher.Core.Steam;

namespace SteamAccountSwitcher
{
    /// <summary>
    /// Top panel item showing the logged-in Steam account; click opens the account list for a manual switch (SPEC §7.4).
    /// Lives on the UI thread.
    /// </summary>
    internal sealed class TopPanel
    {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);

        private readonly SteamAccountSwitcherPlugin plugin;
        private readonly ISteamEnvironment environment;
        private readonly TextBlock text;
        private readonly StackPanel content;
        private readonly DispatcherTimer timer;
        private SteamStatus status;

        public TopPanel(SteamAccountSwitcherPlugin plugin, ISteamEnvironment environment)
        {
            this.plugin = plugin;
            this.environment = environment;

            text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            content = new StackPanel { Orientation = Orientation.Horizontal, Background = System.Windows.Media.Brushes.Transparent };
            var icon = LoadIcon();
            if (icon != null)
            {
                content.Children.Add(new Image { Source = icon, Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center });
            }

            content.Children.Add(text);

            Item = new TopPanelItem
            {
                Icon = content,
                Title = ResourceProvider.GetString("LOCSAS_PluginName"),
                Visible = true,
                Activated = OpenMenu,
            };

            timer = new DispatcherTimer { Interval = RefreshInterval };
            timer.Tick += (s, e) => Refresh();
            Refresh();
            timer.Start();
        }

        public TopPanelItem Item { get; }

        public void Stop() => timer.Stop();

        /// <summary>Re-reads the Steam state (registry and process only; accounts come from the cache).</summary>
        public void Refresh()
        {
            SteamStatus current;
            try
            {
                current = SteamStatus.From(SteamStateReader.Read(environment), plugin.Accounts.Accounts);
            }
            catch (Exception ex)
            {
                LogManager.GetLogger().Warn(ex, "Reading Steam state for the top panel failed.");
                return;
            }

            if (current.Equals(status))
            {
                return;
            }

            status = current;
            string label;
            switch (current.Kind)
            {
                case SteamStatusKind.NotRunning:
                    label = ResourceProvider.GetString("LOCSAS_TopPanelNotRunning");
                    break;
                case SteamStatusKind.LoggingIn:
                    label = ResourceProvider.GetString("LOCSAS_TopPanelLoggingIn");
                    break;
                case SteamStatusKind.LoggedIn:
                    label = string.IsNullOrEmpty(current.Account.PersonaName) ? current.Account.AccountName : current.Account.PersonaName;
                    break;
                default:
                    label = string.Format(ResourceProvider.GetString("LOCSAS_TopPanelUnknown"), current.ActiveUser);
                    break;
            }

            text.Text = label;
            var full = current.Kind == SteamStatusKind.LoggedIn ? current.Account.DisplayName : label;
            content.ToolTip = string.Format(ResourceProvider.GetString("LOCSAS_TopPanelTooltip"), full);
        }

        private void OpenMenu()
        {
            Refresh();
            var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
            if (plugin.IsSwitching)
            {
                menu.Items.Add(new MenuItem { Header = ResourceProvider.GetString("LOCSAS_TopPanelSwitching"), IsEnabled = false });
            }
            else if (plugin.Accounts.Accounts.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = ResourceProvider.GetString("LOCSAS_MenuNoAccounts"), IsEnabled = false });
            }
            else
            {
                foreach (var account in plugin.Accounts.Accounts)
                {
                    var header = SteamAccountSwitcherPlugin.EscapeAccessKeys(account.DisplayName);
                    if (!account.RememberPassword)
                    {
                        header += " – " + ResourceProvider.GetString("LOCSAS_NoRememberedPasswordShort");
                    }

                    var item = new MenuItem
                    {
                        Header = header,
                        IsCheckable = false,
                        IsChecked = status?.Kind == SteamStatusKind.LoggedIn && status.ActiveUser == account.AccountId,
                    };
                    var name = account.AccountName;
                    item.Click += (s, e) => plugin.SwitchManually(name);
                    menu.Items.Add(item);
                }
            }

            menu.IsOpen = true;
        }

        private static BitmapImage LoadIcon()
        {
            var path = Path.Combine(Path.GetDirectoryName(typeof(TopPanel).Assembly.Location) ?? string.Empty, "icon.png");
            if (!File.Exists(path))
            {
                return null;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 36;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
