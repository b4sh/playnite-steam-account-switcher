using System.Collections.Generic;
using System.Windows;
using Playnite.SDK;
using SteamAccountSwitcher.Core.Decision;
using SteamAccountSwitcher.Core.Steam;

namespace SteamAccountSwitcher
{
    /// <summary>
    /// "Ask before start" (SPEC §7.5): Playnite's message box with one option per account, paged
    /// (<see cref="AccountPaging"/>) because the options are laid out in one row. The built-in dialog is styled by
    /// the active theme and supports a controller in Fullscreen mode.
    /// </summary>
    internal static class AccountPicker
    {
        /// <summary>The chosen account, or null when the user cancelled. Must be called on the UI thread.</summary>
        public static SteamAccount Show(IPlayniteAPI api, string gameName, IReadOnlyList<SteamAccount> accounts, SteamState steam)
        {
            var preferred = LaunchTarget.PreferredIndex(accounts, steam);

            // Desktop buttons treat "_" as an access-key marker; Fullscreen buttons show it literally.
            var escape = api.ApplicationInfo.Mode == ApplicationMode.Desktop;
            var total = accounts.Count;
            var page = AccountPaging.PageOf(preferred, total);
            var caption = ResourceProvider.GetString("LOCSAS_PluginName");
            while (true)
            {
                var start = AccountPaging.Start(total, page);
                var count = AccountPaging.Count(total, page);
                var options = new List<MessageBoxOption>();
                var byOption = new Dictionary<MessageBoxOption, SteamAccount>();
                var missingPassword = false;
                for (var i = start; i < start + count; i++)
                {
                    var account = accounts[i];
                    var label = string.IsNullOrEmpty(account.PersonaName) ? account.AccountName : account.PersonaName;
                    if (!account.RememberPassword)
                    {
                        label += " (!)";
                        missingPassword = true;
                    }

                    if (escape)
                    {
                        label = SteamAccountSwitcherPlugin.EscapeAccessKeys(label);
                    }

                    // Playnite treats option titles starting with "LOC" as localization keys; a zero-width space prevents that.
                    if (label.StartsWith("LOC", System.StringComparison.Ordinal))
                    {
                        label = "​" + label;
                    }

                    var option = new MessageBoxOption(label, i == preferred, false);
                    options.Add(option);
                    byOption[option] = account;
                }

                MessageBoxOption more = null;
                if (AccountPaging.HasMore(total))
                {
                    more = new MessageBoxOption(ResourceProvider.GetString("LOCSAS_PickerMore"), false, false);
                    options.Add(more);
                }

                options.Add(new MessageBoxOption(ResourceProvider.GetString("LOCSAS_ButtonCancelStart"), false, true));

                var text = string.Format(ResourceProvider.GetString("LOCSAS_PickerHeader"), gameName)
                    + "\n\n" + CurrentAccountText(accounts, steam);
                if (AccountPaging.HasMore(total))
                {
                    text += "\n" + string.Format(ResourceProvider.GetString("LOCSAS_PickerPage"), start + 1, start + count, total);
                }

                if (missingPassword)
                {
                    text += "\n(!) = " + ResourceProvider.GetString("LOCSAS_NoRememberedPasswordShort");
                }

                var result = api.Dialogs.ShowMessage(text, caption, MessageBoxImage.Question, options);
                if (result != null && result == more)
                {
                    page++;
                    continue;
                }

                return result != null && byOption.TryGetValue(result, out var chosen) ? chosen : null;
            }
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
