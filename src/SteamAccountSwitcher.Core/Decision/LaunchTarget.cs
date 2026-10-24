using System;
using System.Collections.Generic;
using SteamAccountSwitcher.Core.Steam;

namespace SteamAccountSwitcher.Core.Decision
{
    public enum LaunchTargetKind
    {
        /// <summary>No assignment and no default account: leave Steam alone.</summary>
        None,
        Account,

        /// <summary>"Ask before start": the user picks the account each time (SPEC §7.5).</summary>
        Ask,
    }

    /// <summary>Resolves a game's assignment into what to do at game start (SPEC §6 step 1, §7.5). Pure.</summary>
    public sealed class LaunchTarget
    {
        /// <summary>Stored instead of an account name; Steam logins are [A-Za-z0-9_] only, so it cannot collide.</summary>
        public const string AskMarker = "?ask";

        private LaunchTarget(LaunchTargetKind kind, string accountName)
        {
            Kind = kind;
            AccountName = accountName;
        }

        public LaunchTargetKind Kind { get; }

        /// <summary>Target login; set only for <see cref="LaunchTargetKind.Account"/>.</summary>
        public string AccountName { get; }

        public static bool IsAsk(string assignment) => string.Equals(assignment, AskMarker, StringComparison.Ordinal);

        public static LaunchTarget Resolve(string assignment, string defaultAccount)
        {
            if (IsAsk(assignment))
            {
                return new LaunchTarget(LaunchTargetKind.Ask, null);
            }

            if (!string.IsNullOrEmpty(assignment))
            {
                return new LaunchTarget(LaunchTargetKind.Account, assignment);
            }

            return string.IsNullOrEmpty(defaultAccount)
                ? new LaunchTarget(LaunchTargetKind.None, null)
                : new LaunchTarget(LaunchTargetKind.Account, defaultAccount);
        }

        /// <summary>
        /// Index of the account the picker focuses first: the logged-in one, else the one Steam will auto-login to,
        /// else the first; -1 for an empty list.
        /// </summary>
        public static int PreferredIndex(IReadOnlyList<SteamAccount> accounts, SteamState steam)
        {
            if (accounts == null || accounts.Count == 0)
            {
                return -1;
            }

            if (steam != null && steam.IsRunning && steam.ActiveUser != 0)
            {
                for (var i = 0; i < accounts.Count; i++)
                {
                    if (accounts[i].AccountId == steam.ActiveUser)
                    {
                        return i;
                    }
                }
            }

            for (var i = 0; i < accounts.Count; i++)
            {
                if (accounts[i].AutoLogin)
                {
                    return i;
                }
            }

            return 0;
        }

        public override string ToString() => Kind == LaunchTargetKind.Account ? $"{Kind} {AccountName}" : Kind.ToString();
    }
}
