using SteamAccountSwitcher.Core.Decision;
using SteamAccountSwitcher.Core.Steam;
using Xunit;

namespace SteamAccountSwitcher.Core.Tests
{
    /// <summary>One test per row of the SPEC §6 table, plus the edge cases around it.</summary>
    public class LaunchDeciderTests
    {
        private const uint A = 111;
        private const uint B = 222;

        private static LaunchContext Context(SteamState steam, string target = "a_login", uint? targetId = A) =>
            new LaunchContext { Steam = steam, TargetAccountName = target, TargetAccountId = targetId };

        private static SteamState Running(uint activeUser, uint runningAppId = 0, string appName = null) =>
            new SteamState(true, 4242, activeUser, runningAppId, appName);

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void No_assignment_does_nothing(string target)
        {
            var decision = LaunchDecider.Decide(Context(Running(B), target, null));

            Assert.Equal(LaunchAction.None, decision.Action);
        }

        [Fact]
        public void Account_missing_from_loginusers_is_an_error()
        {
            var decision = LaunchDecider.Decide(Context(Running(B), "ghost", null));

            Assert.Equal(LaunchAction.UnknownAccount, decision.Action);
        }

        [Fact]
        public void Steam_not_running_starts_steam()
        {
            var decision = LaunchDecider.Decide(Context(SteamState.NotRunning));

            Assert.Equal(LaunchAction.StartSteam, decision.Action);
            Assert.Equal(SwitchConfirmation.None, decision.Confirmation);
        }

        [Fact]
        public void Null_state_is_treated_as_not_running()
        {
            Assert.Equal(LaunchAction.StartSteam, LaunchDecider.Decide(Context(null)).Action);
        }

        [Fact]
        public void Running_on_target_account_does_nothing()
        {
            Assert.Equal(LaunchAction.None, LaunchDecider.Decide(Context(Running(A))).Action);
        }

        [Fact]
        public void Running_on_target_with_game_running_still_does_nothing()
        {
            Assert.Equal(LaunchAction.None, LaunchDecider.Decide(Context(Running(A, 570, "Dota 2"))).Action);
        }

        [Fact]
        public void Still_logging_in_waits()
        {
            Assert.Equal(LaunchAction.WaitForLogin, LaunchDecider.Decide(Context(Running(0))).Action);
        }

        [Fact]
        public void Other_account_without_game_switches_without_asking()
        {
            var decision = LaunchDecider.Decide(Context(Running(B)));

            Assert.Equal(LaunchAction.SwitchAccount, decision.Action);
            Assert.Equal(SwitchConfirmation.None, decision.Confirmation);
        }

        [Fact]
        public void Other_account_asks_first_when_setting_is_on()
        {
            var context = Context(Running(B));
            context.AskBeforeSwitch = true;

            var decision = LaunchDecider.Decide(context);

            Assert.Equal(LaunchAction.SwitchAccount, decision.Action);
            Assert.Equal(SwitchConfirmation.AskBeforeSwitch, decision.Confirmation);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Other_account_with_steam_game_running_requires_confirmation(bool askBeforeSwitch)
        {
            var context = Context(Running(B, 570, "Dota 2"));
            context.AskBeforeSwitch = askBeforeSwitch;

            var decision = LaunchDecider.Decide(context);

            Assert.Equal(LaunchAction.SwitchAccount, decision.Action);
            Assert.Equal(SwitchConfirmation.GameRunning, decision.Confirmation);
            Assert.Equal("Dota 2", decision.RunningGameName);
        }

        [Fact]
        public void Running_game_without_name_is_described_by_app_id()
        {
            var decision = LaunchDecider.Decide(Context(Running(B, 570)));

            Assert.Equal("AppID 570", decision.RunningGameName);
        }

        [Fact]
        public void Game_tracked_by_playnite_counts_as_running()
        {
            var context = Context(Running(B));
            context.PlayniteRunningSteamGame = "Portal 2";

            var decision = LaunchDecider.Decide(context);

            Assert.Equal(SwitchConfirmation.GameRunning, decision.Confirmation);
            Assert.Equal("Portal 2", decision.RunningGameName);
        }
    }
}
