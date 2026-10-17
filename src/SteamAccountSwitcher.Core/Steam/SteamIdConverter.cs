using System;

namespace SteamAccountSwitcher.Core.Steam
{
    /// <summary>SteamID64 &lt;-&gt; 32-bit AccountId (the value Steam stores in ActiveProcess\ActiveUser).</summary>
    public static class SteamIdConverter
    {
        /// <summary>SteamID64 of AccountId 0 for an individual account in the public universe.</summary>
        public const ulong IndividualBase = 76561197960265728UL;

        public static uint ToAccountId(ulong steamId64)
        {
            if (steamId64 < IndividualBase || steamId64 - IndividualBase > uint.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(steamId64), steamId64, "Not an individual public-universe SteamID64.");
            }

            return (uint)(steamId64 - IndividualBase);
        }

        public static ulong ToSteamId64(uint accountId) => IndividualBase + accountId;

        public static bool TryParseAccountId(string steamId64, out uint accountId)
        {
            accountId = 0;
            if (!ulong.TryParse(steamId64, out var id) || id < IndividualBase || id - IndividualBase > uint.MaxValue)
            {
                return false;
            }

            accountId = (uint)(id - IndividualBase);
            return true;
        }
    }
}
