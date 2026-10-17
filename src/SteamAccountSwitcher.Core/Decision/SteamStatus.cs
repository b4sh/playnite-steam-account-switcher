using System.Collections.Generic;
using System.Linq;
using SteamAccountSwitcher.Core.Steam;

namespace SteamAccountSwitcher.Core.Decision
{
    public enum SteamStatusKind
    {
        NotRunning,
        LoggingIn,
        LoggedIn,

        /// <summary>Logged in, but the account is not in loginusers.vdf (or the cache is stale).</summary>
        UnknownAccount,
    }

    /// <summary>What the top panel shows (SPEC §7.4). Pure; compare with <see cref="Equals(object)"/> to detect changes.</summary>
    public sealed class SteamStatus
    {
        private SteamStatus(SteamStatusKind kind, uint activeUser, SteamAccount account)
        {
            Kind = kind;
            ActiveUser = activeUser;
            Account = account;
        }

        public SteamStatusKind Kind { get; }

        public uint ActiveUser { get; }

        /// <summary>Logged-in account; null unless <see cref="Kind"/> is <see cref="SteamStatusKind.LoggedIn"/>.</summary>
        public SteamAccount Account { get; }

        public static SteamStatus From(SteamState state, IReadOnlyList<SteamAccount> accounts)
        {
            if (state == null || !state.IsRunning)
            {
                return new SteamStatus(SteamStatusKind.NotRunning, 0, null);
            }

            if (state.ActiveUser == 0)
            {
                return new SteamStatus(SteamStatusKind.LoggingIn, 0, null);
            }

            var account = accounts?.FirstOrDefault(a => a.AccountId == state.ActiveUser);
            return account == null
                ? new SteamStatus(SteamStatusKind.UnknownAccount, state.ActiveUser, null)
                : new SteamStatus(SteamStatusKind.LoggedIn, state.ActiveUser, account);
        }

        public override bool Equals(object obj) =>
            obj is SteamStatus other
            && other.Kind == Kind
            && other.ActiveUser == ActiveUser
            && other.Account?.PersonaName == Account?.PersonaName;

        public override int GetHashCode() => ((int)Kind * 397) ^ (int)ActiveUser;

        public override string ToString() => Kind == SteamStatusKind.LoggedIn ? $"{Kind} {Account.AccountName}" : $"{Kind} {ActiveUser}";
    }
}
