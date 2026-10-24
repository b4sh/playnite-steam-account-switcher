using SteamAccountSwitcher.Core.Decision;
using SteamAccountSwitcher.Core.Steam;
using Xunit;

namespace SteamAccountSwitcher.Core.Tests
{
    public class LaunchTargetTests
    {
        private static readonly SteamAccount Alpha = new SteamAccount(SteamIdConverter.ToSteamId64(111), "alpha_login", "Alpha", true, false);
        private static readonly SteamAccount Beta = new SteamAccount(SteamIdConverter.ToSteamId64(222), "beta_login", "Beta", true, true);
        private static readonly SteamAccount Gamma = new SteamAccount(SteamIdConverter.ToSteamId64(333), "gamma_login", "Gamma", false, false);

        private static SteamState Running(uint activeUser) => new SteamState(true, 4242, activeUser, 0, null);

        [Fact]
        public void Assigned_account_wins_over_default()
        {
            var target = LaunchTarget.Resolve("alpha_login", "beta_login");

            Assert.Equal(LaunchTargetKind.Account, target.Kind);
            Assert.Equal("alpha_login", target.AccountName);
        }

        [Fact]
        public void No_assignment_uses_default_account()
        {
            var target = LaunchTarget.Resolve(null, "beta_login");

            Assert.Equal(LaunchTargetKind.Account, target.Kind);
            Assert.Equal("beta_login", target.AccountName);
        }

        [Fact]
        public void No_assignment_and_no_default_does_nothing()
        {
            Assert.Equal(LaunchTargetKind.None, LaunchTarget.Resolve(null, null).Kind);
            Assert.Equal(LaunchTargetKind.None, LaunchTarget.Resolve(string.Empty, string.Empty).Kind);
        }

        [Fact]
        public void Ask_marker_asks_even_with_default_account()
        {
            var target = LaunchTarget.Resolve(LaunchTarget.AskMarker, "beta_login");

            Assert.Equal(LaunchTargetKind.Ask, target.Kind);
            Assert.Null(target.AccountName);
        }

        [Fact]
        public void Ask_marker_is_not_a_valid_steam_login()
        {
            Assert.True(LaunchTarget.IsAsk("?ask"));
            Assert.False(LaunchTarget.IsAsk("ask"));
            Assert.False(LaunchTarget.IsAsk("?ASK"));
            Assert.False(LaunchTarget.IsAsk(null));
        }

        [Fact]
        public void Picker_focuses_logged_in_account()
        {
            Assert.Equal(0, LaunchTarget.PreferredIndex(new[] { Alpha, Beta, Gamma }, Running(111)));
            Assert.Equal(2, LaunchTarget.PreferredIndex(new[] { Alpha, Beta, Gamma }, Running(333)));
        }

        [Fact]
        public void Picker_falls_back_to_auto_login_account()
        {
            var accounts = new[] { Alpha, Beta, Gamma };

            Assert.Equal(1, LaunchTarget.PreferredIndex(accounts, SteamState.NotRunning));
            Assert.Equal(1, LaunchTarget.PreferredIndex(accounts, Running(0)));
            Assert.Equal(1, LaunchTarget.PreferredIndex(accounts, Running(999)));
            Assert.Equal(1, LaunchTarget.PreferredIndex(accounts, null));
        }

        [Fact]
        public void Picker_falls_back_to_first_account()
        {
            Assert.Equal(0, LaunchTarget.PreferredIndex(new[] { Alpha, Gamma }, SteamState.NotRunning));
        }

        [Fact]
        public void Picker_without_accounts_has_no_focus()
        {
            Assert.Equal(-1, LaunchTarget.PreferredIndex(new SteamAccount[0], Running(111)));
            Assert.Equal(-1, LaunchTarget.PreferredIndex(null, Running(111)));
        }
    }
}
