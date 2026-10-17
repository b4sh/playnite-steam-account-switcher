using SteamAccountSwitcher.Core.Steam;

namespace SteamAccountSwitcher.Core.Decision
{
    public enum LaunchAction
    {
        /// <summary>Let the game start without touching Steam.</summary>
        None,

        /// <summary>Steam is not running: set AutoLoginUser, start Steam, wait for login.</summary>
        StartSteam,

        /// <summary>Steam is still logging in: wait for any account, then decide again.</summary>
        WaitForLogin,

        /// <summary>Steam runs on another account: shutdown, set AutoLoginUser, start, wait for login.</summary>
        SwitchAccount,

        /// <summary>The assigned account is not in loginusers.vdf; cancel the game start with an error.</summary>
        UnknownAccount,
    }

    public enum SwitchConfirmation
    {
        None,

        /// <summary>The user asked to be asked before every switch (setting).</summary>
        AskBeforeSwitch,

        /// <summary>A game runs on the current account; closing Steam ends it. Required regardless of settings.</summary>
        GameRunning,
    }

    public sealed class LaunchContext
    {
        public SteamState Steam { get; set; } = SteamState.NotRunning;

        /// <summary>Assigned (or default) account name; null/empty = no assignment.</summary>
        public string TargetAccountName { get; set; }

        /// <summary>AccountId of the target from loginusers.vdf; null when the account is not there.</summary>
        public uint? TargetAccountId { get; set; }

        public bool AskBeforeSwitch { get; set; }

        /// <summary>Name of a Steam game Playnite tracks as running (in addition to RunningAppID); null = none.</summary>
        public string PlayniteRunningSteamGame { get; set; }
    }

    public sealed class LaunchDecision
    {
        public LaunchDecision(LaunchAction action, SwitchConfirmation confirmation = SwitchConfirmation.None, string runningGameName = null)
        {
            Action = action;
            Confirmation = confirmation;
            RunningGameName = runningGameName;
        }

        public LaunchAction Action { get; }

        /// <summary>For <see cref="LaunchAction.SwitchAccount"/>: what to ask the user first; declining cancels the game start.</summary>
        public SwitchConfirmation Confirmation { get; }

        /// <summary>For <see cref="SwitchConfirmation.GameRunning"/>: display name of the running game.</summary>
        public string RunningGameName { get; }

        public override string ToString() => Confirmation == SwitchConfirmation.None ? Action.ToString() : $"{Action} ({Confirmation})";
    }

    /// <summary>Pure decision of what to do before a game starts (SPEC §6 table).</summary>
    public static class LaunchDecider
    {
        public static LaunchDecision Decide(LaunchContext context)
        {
            if (string.IsNullOrEmpty(context.TargetAccountName))
            {
                return new LaunchDecision(LaunchAction.None);
            }

            if (context.TargetAccountId == null)
            {
                return new LaunchDecision(LaunchAction.UnknownAccount);
            }

            var steam = context.Steam ?? SteamState.NotRunning;
            if (!steam.IsRunning)
            {
                return new LaunchDecision(LaunchAction.StartSteam);
            }

            if (steam.ActiveUser == context.TargetAccountId.Value)
            {
                return new LaunchDecision(LaunchAction.None);
            }

            if (steam.ActiveUser == 0)
            {
                return new LaunchDecision(LaunchAction.WaitForLogin);
            }

            if (steam.RunningAppId != 0)
            {
                var name = string.IsNullOrEmpty(steam.RunningAppName) ? $"AppID {steam.RunningAppId}" : steam.RunningAppName;
                return new LaunchDecision(LaunchAction.SwitchAccount, SwitchConfirmation.GameRunning, name);
            }

            if (!string.IsNullOrEmpty(context.PlayniteRunningSteamGame))
            {
                return new LaunchDecision(LaunchAction.SwitchAccount, SwitchConfirmation.GameRunning, context.PlayniteRunningSteamGame);
            }

            return new LaunchDecision(
                LaunchAction.SwitchAccount,
                context.AskBeforeSwitch ? SwitchConfirmation.AskBeforeSwitch : SwitchConfirmation.None);
        }
    }
}
