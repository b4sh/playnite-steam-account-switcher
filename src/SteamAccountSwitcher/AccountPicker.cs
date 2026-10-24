using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Events;
using SteamAccountSwitcher.Core.Decision;
using SteamAccountSwitcher.Core.Steam;

namespace SteamAccountSwitcher
{
    /// <summary>
    /// "Ask before start" (SPEC §7.5): Playnite's message box with one option per account, paged
    /// (<see cref="AccountPaging"/>) because the options are laid out in one row. The built-in dialog is styled by
    /// the active theme and supports a controller in Fullscreen mode. UI thread only.
    /// </summary>
    internal static class AccountPicker
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private static readonly TimeSpan ActivationTimeout = TimeSpan.FromMilliseconds(500);

        /// <summary>The window that was active when the picker opened; non-null while a picker page is shown.</summary>
        private static Window openOwner;

        /// <summary>The chosen account, or null when the user cancelled.</summary>
        public static SteamAccount Show(IPlayniteAPI api, string gameName, IReadOnlyList<SteamAccount> accounts, SteamState steam)
        {
            var desktop = api.ApplicationInfo.Mode == ApplicationMode.Desktop;

            // Fullscreen saves the room of a Cancel button: the controller's cancel button closes the picker.
            var paging = new AccountPaging(accounts.Count, desktop ? 1 : 0);
            var preferred = LaunchTarget.PreferredIndex(accounts, steam);
            var page = paging.PageOf(preferred);
            var caption = ResourceProvider.GetString("LOCSAS_PluginName");
            var owner = ActiveWindow();
            try
            {
                for (var first = true; ; first = false)
                {
                    if (!first)
                    {
                        ReactivateOwner(owner);
                    }

                    var start = paging.Start(page);
                    var count = paging.Count(page);
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

                        var option = new MessageBoxOption(OptionTitle(label, desktop), i == preferred, false);
                        options.Add(option);
                        byOption[option] = account;
                    }

                    MessageBoxOption more = null;
                    if (paging.HasMore)
                    {
                        more = new MessageBoxOption(ResourceProvider.GetString("LOCSAS_PickerMore"), false, false);
                        options.Add(more);
                    }

                    if (desktop)
                    {
                        options.Add(new MessageBoxOption(ResourceProvider.GetString("LOCSAS_ButtonCancelStart"), false, true));
                    }

                    var text = string.Format(ResourceProvider.GetString("LOCSAS_PickerHeader"), gameName)
                        + "\n\n" + CurrentAccountText(accounts, steam);
                    if (paging.HasMore)
                    {
                        text += "\n" + string.Format(ResourceProvider.GetString("LOCSAS_PickerPage"), start + 1, start + count, paging.Total);
                    }

                    if (missingPassword)
                    {
                        text += "\n(!) = " + ResourceProvider.GetString("LOCSAS_NoRememberedPasswordShort");
                    }

                    openOwner = owner;
                    var result = api.Dialogs.ShowMessage(text, caption, MessageBoxImage.Question, options);
                    openOwner = null;
                    if (result != null && result == more)
                    {
                        page++;
                        continue;
                    }

                    return result != null && byOption.TryGetValue(result, out var chosen) ? chosen : null;
                }
            }
            finally
            {
                openOwner = null;
            }
        }

        /// <summary>
        /// Playnite's Fullscreen message box has no handling for the controller's cancel button, so the plugin closes
        /// an open picker page itself (closing it without choosing = cancelled start).
        /// </summary>
        public static void HandleControllerButton(IPlayniteAPI api, OnControllerButtonStateChangedArgs args)
        {
            if (openOwner == null || args.State != ControllerInputState.Pressed)
            {
                return;
            }

            if (args.Button != CancelButton(api))
            {
                return;
            }

            var box = ActiveWindow();
            if (box != null && box != openOwner)
            {
                box.Close();
            }
        }

        private static ControllerInput CancelButton(IPlayniteAPI api) =>
            api.ApplicationSettings.Fullscreen.SwapConfirmCancelButtons ? ControllerInput.A : ControllerInput.B;

        private static string OptionTitle(string label, bool desktop)
        {
            // Desktop buttons treat "_" as an access-key marker; Fullscreen buttons show it literally.
            if (desktop)
            {
                label = SteamAccountSwitcherPlugin.EscapeAccessKeys(label);
            }

            // Playnite treats option titles starting with "LOC" as localization keys; a zero-width space prevents that.
            return label.StartsWith("LOC", StringComparison.Ordinal) ? "​" + label : label;
        }

        private static Window ActiveWindow() =>
            Application.Current?.Windows.OfType<Window>().LastOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

        /// <summary>
        /// Playnite gives a new message box the *active* window as owner and sends controller input there. Right after
        /// the previous page closes no window may be active yet: re-activate the original window and wait (bounded)
        /// until it is active and has keyboard focus before showing the next page.
        /// </summary>
        private static void ReactivateOwner(Window owner)
        {
            if (owner == null || !owner.IsLoaded)
            {
                return;
            }

            owner.Activate();
            var deadline = DateTime.UtcNow + ActivationTimeout;
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Background, (s, e) =>
            {
                if ((owner.IsActive && Keyboard.PrimaryDevice.ActiveSource != null) || DateTime.UtcNow > deadline)
                {
                    ((DispatcherTimer)s).Stop();
                    frame.Continue = false;
                }
            }, owner.Dispatcher);
            timer.Start();
            Dispatcher.PushFrame(frame);

            Logger.Info($"Account picker page switch: owner active={owner.IsActive}, keyboard source={(Keyboard.PrimaryDevice.ActiveSource != null)}, " +
                $"focused={Keyboard.FocusedElement?.GetType().Name ?? "none"}, active window={ActiveWindow()?.GetType().Name ?? "none"}.");
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
