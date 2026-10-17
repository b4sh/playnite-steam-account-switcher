using System.Collections.Generic;
using System.Windows;
using Playnite.SDK;
using SteamAccountSwitcher.Core.Decision;
using SteamAccountSwitcher.Core.Steam;

namespace SteamAccountSwitcher
{
    /// <summary>Asks launch questions with Playnite dialogs on the UI thread (the coordinator runs in a progress task).</summary>
    internal sealed class PlayniteLaunchInteraction : ILaunchInteraction
    {
        private readonly IPlayniteAPI api;

        public PlayniteLaunchInteraction(IPlayniteAPI api)
        {
            this.api = api;
        }

        public bool ConfirmSwitch(SwitchConfirmation kind, SteamAccount target, SteamAccount current, string runningGameName)
        {
            var currentName = current?.DisplayName ?? ResourceProvider.GetString("LOCSAS_UnknownAccount");
            string text;
            MessageBoxImage icon;
            if (kind == SwitchConfirmation.GameRunning)
            {
                text = string.Format(ResourceProvider.GetString("LOCSAS_ConfirmSwitchGameRunning"), currentName, runningGameName, target.DisplayName);
                icon = MessageBoxImage.Warning;
            }
            else
            {
                text = string.Format(ResourceProvider.GetString("LOCSAS_ConfirmSwitch"), currentName, target.DisplayName);
                icon = MessageBoxImage.Question;
            }

            var caption = ResourceProvider.GetString("LOCSAS_PluginName");
            var result = api.MainView.UIDispatcher.Invoke(() => api.Dialogs.ShowMessage(text, caption, MessageBoxButton.YesNo, icon));
            return result == MessageBoxResult.Yes;
        }

        public bool ConfirmKeepWaiting(SwitchWait wait, SteamAccount target)
        {
            var text = wait == SwitchWait.Shutdown
                ? ResourceProvider.GetString("LOCSAS_KeepWaitingShutdown")
                : string.Format(ResourceProvider.GetString("LOCSAS_KeepWaitingLogin"), target?.DisplayName);
            var keepWaiting = new MessageBoxOption(ResourceProvider.GetString("LOCSAS_ButtonKeepWaiting"), true, false);
            var cancel = new MessageBoxOption(ResourceProvider.GetString("LOCSAS_ButtonCancelStart"), false, true);
            var caption = ResourceProvider.GetString("LOCSAS_PluginName");
            var result = api.MainView.UIDispatcher.Invoke(() =>
                api.Dialogs.ShowMessage(text, caption, MessageBoxImage.Question, new List<MessageBoxOption> { keepWaiting, cancel }));
            return result == keepWaiting;
        }
    }
}
