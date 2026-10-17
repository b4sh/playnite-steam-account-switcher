using System.Collections.Generic;
using SteamAccountSwitcher.Core.Steam.Vdf;

namespace SteamAccountSwitcher.Core.Steam
{
    /// <summary>
    /// Reads {Steam}\config\loginusers.vdf. Never writes it: M0 showed the registry alone is enough
    /// to switch accounts and Steam maintains this file itself (SPEC §5.2, §5.4).
    /// </summary>
    public static class LoginUsersFile
    {
        public static IReadOnlyList<SteamAccount> Load(ISteamEnvironment environment, string path)
        {
            if (!environment.FileExists(path))
            {
                return new SteamAccount[0];
            }

            return Parse(environment.ReadAllText(path));
        }

        /// <summary>Accounts in file order (Steam keeps the most recently used one first). Invalid entries are skipped.</summary>
        public static IReadOnlyList<SteamAccount> Parse(string text)
        {
            var result = new List<SteamAccount>();
            var users = TextVdf.Parse(text).GetObject("users");
            if (users == null)
            {
                return result;
            }

            foreach (var key in users.Keys)
            {
                var user = users.GetObject(key);
                if (user == null || !ulong.TryParse(key, out var steamId64) || !SteamIdConverter.TryParseAccountId(key, out _))
                {
                    continue;
                }

                var accountName = user.GetString("AccountName");
                if (string.IsNullOrEmpty(accountName))
                {
                    continue;
                }

                // Current clients write AutoLogin; older ones wrote MostRecent/AllowAutoLogin (SPEC §5.2).
                var autoLogin = user.GetString("AutoLogin") != null
                    ? IsTrue(user.GetString("AutoLogin"))
                    : IsTrue(user.GetString("MostRecent"));

                result.Add(new SteamAccount(
                    steamId64,
                    accountName,
                    user.GetString("PersonaName"),
                    IsTrue(user.GetString("RememberPassword")),
                    autoLogin));
            }

            return result;
        }

        private static bool IsTrue(string value) => value == "1";
    }
}
