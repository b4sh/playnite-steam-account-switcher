using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Playnite.SDK;
using Playnite.SDK.Events;
using SteamAccountSwitcher.Core.Decision;
using SteamAccountSwitcher.Core.Steam;

namespace SteamAccountSwitcher
{
    /// <summary>
    /// "Ask before start" window (SPEC §7.5): a vertical list of account buttons plus Cancel, styled by the active theme.
    /// Controller: Playnite sends D-pad/stick directions to the active window as arrow keys, but confirm/cancel buttons
    /// only reach its own windows, so the plugin forwards them via <see cref="HandleControllerButton"/>.
    /// UI thread only.
    /// </summary>
    internal sealed class AccountPicker
    {
        private static AccountPicker current;

        private readonly Window window;
        private readonly bool swapConfirmCancel;
        private SteamAccount chosen;

        private AccountPicker(IPlayniteAPI api, string gameName, IReadOnlyList<SteamAccount> accounts, SteamState steam)
        {
            swapConfirmCancel = api.ApplicationInfo.Mode == ApplicationMode.Fullscreen
                && api.ApplicationSettings.Fullscreen.SwapConfirmCancelButtons;

            window = api.Dialogs.CreateWindow(new WindowCreationOptions
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

            var panel = new StackPanel { Margin = new Thickness(20), MinWidth = 360 };
            KeyboardNavigation.SetDirectionalNavigation(panel, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetTabNavigation(panel, KeyboardNavigationMode.Cycle);

            panel.Children.Add(Text(string.Format(ResourceProvider.GetString("LOCSAS_PickerHeader"), gameName), new Thickness(0, 0, 0, 6)));
            panel.Children.Add(Text(CurrentAccountText(accounts, steam), new Thickness(0, 0, 0, 14)));

            var buttons = new List<Button>();
            foreach (var account in accounts)
            {
                var label = account.RememberPassword
                    ? account.DisplayName
                    : account.DisplayName + " – " + ResourceProvider.GetString("LOCSAS_NoRememberedPasswordShort");
                var button = NewButton(label);
                button.Margin = new Thickness(0, 0, 0, 6);
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                var target = account;
                button.Click += (s, e) =>
                {
                    chosen = target;
                    window.DialogResult = true;
                };
                buttons.Add(button);
                panel.Children.Add(button);
            }

            var cancel = NewButton(ResourceProvider.GetString("LOCSAS_ButtonCancelStart"));
            cancel.IsCancel = true;
            cancel.Margin = new Thickness(0, 14, 0, 0);
            cancel.HorizontalAlignment = HorizontalAlignment.Right;
            panel.Children.Add(cancel);

            window.Content = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = SystemParameters.WorkArea.Height * 0.8,
            };

            var preferred = LaunchTarget.PreferredIndex(accounts, steam);
            window.Loaded += (s, e) => (preferred >= 0 ? buttons[preferred] : cancel).Focus();
        }

        /// <summary>The chosen account, or null when the user cancelled.</summary>
        public static SteamAccount Show(IPlayniteAPI api, string gameName, IReadOnlyList<SteamAccount> accounts, SteamState steam)
        {
            var picker = new AccountPicker(api, gameName, accounts, steam);
            current = picker;
            try
            {
                return picker.window.ShowDialog() == true ? picker.chosen : null;
            }
            finally
            {
                current = null;
            }
        }

        /// <summary>Controller confirm clicks the focused button, cancel closes the picker. No-op when no picker is open.</summary>
        public static void HandleControllerButton(OnControllerButtonStateChangedArgs args)
        {
            var picker = current;
            if (picker == null || args.State != ControllerInputState.Pressed || !picker.window.IsActive)
            {
                return;
            }

            var confirm = picker.swapConfirmCancel ? ControllerInput.B : ControllerInput.A;
            var cancel = picker.swapConfirmCancel ? ControllerInput.A : ControllerInput.B;
            if (args.Button == confirm)
            {
                (Keyboard.FocusedElement as ButtonBase)?.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            else if (args.Button == cancel)
            {
                picker.window.DialogResult = false;
            }
        }

        private static TextBlock Text(string text, Thickness margin)
        {
            var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 520, Margin = margin };
            block.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            return block;
        }

        // TextBlock content: a plain string would treat "_" in logins as an access-key marker in some themes.
        private static Button NewButton(string label) => new Button
        {
            Content = new TextBlock { Text = label },
            Padding = new Thickness(14, 8, 14, 8),
        };

        private static string CurrentAccountText(IReadOnlyList<SteamAccount> accounts, SteamState steam)
        {
            var status = SteamStatus.From(steam, accounts);
            switch (status.Kind)
            {
                case SteamStatusKind.NotRunning:
                    return ResourceProvider.GetString("LOCSAS_TopPanelNotRunning");
                case SteamStatusKind.LoggingIn:
                    return ResourceProvider.GetString("LOCSAS_TopPanelLoggingIn");
                case SteamStatusKind.LoggedIn:
                    return string.Format(ResourceProvider.GetString("LOCSAS_PickerLoggedIn"), status.Account.DisplayName);
                default:
                    return string.Format(ResourceProvider.GetString("LOCSAS_TopPanelUnknown"), status.ActiveUser);
            }
        }
    }
}
