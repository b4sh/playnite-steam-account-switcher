using SteamAccountSwitcher.Core.Decision;
using SteamAccountSwitcher.Core.Steam;
using Xunit;

namespace SteamAccountSwitcher.Core.Tests
{
    public class SteamStatusTests
    {
        private static readonly SteamAccount Alpha = new SteamAccount(SteamIdConverter.ToSteamId64(111), "a_login", "Alpha", true, false);
        private static readonly SteamAccount[] Accounts = { Alpha, new SteamAccount(SteamIdConverter.ToSteamId64(222), "b_login", "Beta", true, true) };

        private static SteamState Running(uint activeUser) => new SteamState(true, 4242, activeUser, 0, null);

        [Fact]
        public void Not_running()
        {
            Assert.Equal(SteamStatusKind.NotRunning, SteamStatus.From(SteamState.NotRunning, Accounts).Kind);
            Assert.Equal(SteamStatusKind.NotRunning, SteamStatus.From(null, Accounts).Kind);
        }

        [Fact]
        public void Logging_in()
        {
            Assert.Equal(SteamStatusKind.LoggingIn, SteamStatus.From(Running(0), Accounts).Kind);
        }

        [Fact]
        public void Logged_in_resolves_account()
        {
            var status = SteamStatus.From(Running(111), Accounts);

            Assert.Equal(SteamStatusKind.LoggedIn, status.Kind);
            Assert.Same(Alpha, status.Account);
        }

        [Fact]
        public void Logged_in_account_missing_from_list_is_unknown()
        {
            var status = SteamStatus.From(Running(999), Accounts);

            Assert.Equal(SteamStatusKind.UnknownAccount, status.Kind);
            Assert.Equal(999u, status.ActiveUser);
            Assert.Null(status.Account);
            Assert.Equal(SteamStatusKind.UnknownAccount, SteamStatus.From(Running(111), null).Kind);
        }

        [Fact]
        public void Equality_detects_changes_only()
        {
            Assert.Equal(SteamStatus.From(Running(111), Accounts), SteamStatus.From(Running(111), Accounts));
            Assert.NotEqual(SteamStatus.From(Running(111), Accounts), SteamStatus.From(Running(222), Accounts));
            Assert.NotEqual(SteamStatus.From(Running(0), Accounts), SteamStatus.From(SteamState.NotRunning, Accounts));

            var renamed = new[] { new SteamAccount(SteamIdConverter.ToSteamId64(111), "a_login", "Alpha Renamed", true, false) };
            Assert.NotEqual(SteamStatus.From(Running(111), Accounts), SteamStatus.From(Running(111), renamed));
        }
    }
}
