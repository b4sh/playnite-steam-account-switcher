namespace SteamAccountSwitcher.Core.Steam
{
    /// <summary>One entry of loginusers.vdf. Contains no secrets.</summary>
    public sealed class SteamAccount
    {
        public SteamAccount(ulong steamId64, string accountName, string personaName, bool rememberPassword, bool autoLogin)
        {
            SteamId64 = steamId64;
            AccountId = SteamIdConverter.ToAccountId(steamId64);
            AccountName = accountName;
            PersonaName = personaName;
            RememberPassword = rememberPassword;
            AutoLogin = autoLogin;
        }

        public ulong SteamId64 { get; }

        public uint AccountId { get; }

        /// <summary>Login name; what assignments are stored by and what AutoLoginUser takes.</summary>
        public string AccountName { get; }

        public string PersonaName { get; }

        /// <summary>False means Steam will ask for a password when switching to this account (SPEC §5.2).</summary>
        public bool RememberPassword { get; }

        /// <summary>The account Steam will log into on next start (AutoLogin, or MostRecent in older clients).</summary>
        public bool AutoLogin { get; }

        public string DisplayName => string.IsNullOrEmpty(PersonaName) ? AccountName : $"{PersonaName} ({AccountName})";

        public override string ToString() => DisplayName;
    }
}
