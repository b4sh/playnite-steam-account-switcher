using System.Linq;
using SteamAccountSwitcher.Core.Steam;
using SteamAccountSwitcher.Core.Tests.Fakes;
using Xunit;

namespace SteamAccountSwitcher.Core.Tests
{
    public class LoginUsersFileTests
    {
        // Layout of a current (2026) client file, as captured in M0; names are made up.
        private const string CurrentFormat =
            "\"users\"\n{\n" +
            "\t\"76561197960287930\"\n\t{\n" +
            "\t\t\"AccountName\"\t\t\"alpha_login\"\n" +
            "\t\t\"PersonaName\"\t\t\"Alpha\"\n" +
            "\t\t\"RememberPassword\"\t\t\"1\"\n" +
            "\t\t\"WantsOfflineMode\"\t\t\"0\"\n" +
            "\t\t\"SkipOfflineModeWarning\"\t\t\"0\"\n" +
            "\t\t\"AutoLogin\"\t\t\"1\"\n" +
            "\t\t\"Timestamp\"\t\t\"1792256083\"\n" +
            "\t}\n" +
            "\t\"76561197960265729\"\n\t{\n" +
            "\t\t\"AccountName\"\t\t\"beta_login\"\n" +
            "\t\t\"PersonaName\"\t\t\"Beta\"\n" +
            "\t\t\"RememberPassword\"\t\t\"0\"\n" +
            "\t\t\"AutoLogin\"\t\t\"0\"\n" +
            "\t}\n" +
            "}\n";

        // Older clients: MostRecent/AllowAutoLogin instead of AutoLogin.
        private const string LegacyFormat =
            "\"users\" {\r\n" +
            "  \"76561197960265730\" { \"AccountName\" \"gamma\" \"PersonaName\" \"Gamma\" \"RememberPassword\" \"1\" \"MostRecent\" \"1\" \"AllowAutoLogin\" \"1\" }\r\n" +
            "  \"76561197960265731\" { \"AccountName\" \"delta\" \"RememberPassword\" \"1\" \"MostRecent\" \"0\" }\r\n" +
            "}\r\n";

        [Fact]
        public void Parses_current_format()
        {
            var accounts = LoginUsersFile.Parse(CurrentFormat);

            Assert.Equal(2, accounts.Count);
            var alpha = accounts[0];
            Assert.Equal(76561197960287930UL, alpha.SteamId64);
            Assert.Equal(22202u, alpha.AccountId);
            Assert.Equal("alpha_login", alpha.AccountName);
            Assert.Equal("Alpha", alpha.PersonaName);
            Assert.True(alpha.RememberPassword);
            Assert.True(alpha.AutoLogin);
            Assert.Equal("Alpha (alpha_login)", alpha.DisplayName);

            var beta = accounts[1];
            Assert.Equal(1u, beta.AccountId);
            Assert.False(beta.RememberPassword);
            Assert.False(beta.AutoLogin);
        }

        [Fact]
        public void Parses_legacy_format_using_MostRecent_as_AutoLogin()
        {
            var accounts = LoginUsersFile.Parse(LegacyFormat);

            Assert.Equal(new[] { "gamma", "delta" }, accounts.Select(a => a.AccountName));
            Assert.True(accounts[0].AutoLogin);
            Assert.False(accounts[1].AutoLogin);
            Assert.Equal("delta", accounts[1].DisplayName);
        }

        [Fact]
        public void Skips_entries_with_invalid_id_or_missing_name()
        {
            var text = "\"users\" { " +
                       "\"garbage\" { \"AccountName\" \"x\" } " +
                       "\"76561197960265732\" { \"PersonaName\" \"no login\" } " +
                       "\"76561197960265733\" \"not an object\" " +
                       "\"76561197960265734\" { \"AccountName\" \"ok\" } }";

            Assert.Equal(new[] { "ok" }, LoginUsersFile.Parse(text).Select(a => a.AccountName));
        }

        [Theory]
        [InlineData("")]
        [InlineData("\"users\" { }")]
        [InlineData("\"something\" { \"else\" \"1\" }")]
        public void Returns_empty_list_without_users(string text)
        {
            Assert.Empty(LoginUsersFile.Parse(text));
        }

        [Fact]
        public void Load_returns_empty_when_file_is_missing()
        {
            Assert.Empty(LoginUsersFile.Load(new FakeSteamEnvironment(), @"C:\Steam\config\loginusers.vdf"));
        }

        [Fact]
        public void Load_reads_through_environment()
        {
            var env = new FakeSteamEnvironment();
            env.Files[@"C:\Steam\config\loginusers.vdf"] = CurrentFormat;

            Assert.Equal(2, LoginUsersFile.Load(env, @"C:\Steam\config\loginusers.vdf").Count);
        }
    }
}
