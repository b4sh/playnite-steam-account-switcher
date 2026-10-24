using System.Collections.Generic;
using System.Windows;
using Playnite.SDK;
using SteamAccountSwitcher.Core.Decision;
using SteamAccountSwitcher.Core.Steam;

namespace SteamAccountSwitcher
{
    /// <summary>
    /// "Ask before start" (SPEC §7.5): Playnite's message box with one option per account plus Cancel.
    /// The built-in dialog is styled by the active theme and supports a controller in Fullscreen mode
    /// (Fullscreen does not implement ChooseItemWithSearch, and custom windows don't receive controller input).
    /// </summary>
    internal static class AccountPicker
    {
        /// <summary>The chosen account, or null when the user cancelled. Must be called on the UI thread.</summary>
        public static SteamAccount Show(IPlayniteAPI api, string gameName, IReadOnlyList<SteamAccount> accounts, SteamState steam)
        {
            var preferred = LaunchTarget.PreferredIndex(accounts, steam);

            // Desktop buttons treat "_" as an access-key marker; Fullscreen buttons show it literally.
            var escape = api.ApplicationInfo.Mode == ApplicationMode.Desktop;
            var options = new List<MessageBoxOption>();
            var byOption = new Dictionary<MessageBoxOption, SteamAccount>();
            for (var i = 0; i < accounts.Count; i++)
            {
                var account = accounts[i];
                var label = account.RememberPassword
                    ? account.DisplayName
                    : account.DisplayName + " – " + ResourceProvider.GetString("LOCSAS_NoRememberedPasswordShort");
                var option = new MessageBoxOption(escape ? SteamAccountSwitcherPlugin.EscapeAccessKeys(label) : label, i == preferred, false);
                options.Add(option);
                byOption[option] = account;
            }

            options.Add(new MessageBoxOption(ResourceProvider.GetString("LOCSAS_ButtonCancelStart"), preferred < 0, true));

            var text = string.Format(ResourceProvider.GetString("LOCSAS_PickerHeader"), gameName)
                + "\n\n" + CurrentAccountText(accounts, steam);
            var result = api.Dialogs.ShowMessage(text, ResourceProvider.GetString("LOCSAS_PluginName"), MessageBoxImage.Question, options);
            return result != null && byOption.TryGetValue(result, out var chosen) ? chosen : null;
        }

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
