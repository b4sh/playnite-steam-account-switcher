using System;
using System.Collections.Generic;
using System.Threading;
using SteamAccountSwitcher.Core.Decision;
using SteamAccountSwitcher.Core.Steam;
using SteamAccountSwitcher.Core.Tests.Fakes;
using Xunit;

namespace SteamAccountSwitcher.Core.Tests
{
    /// <summary>End-to-end launch preparation on the Steam simulator; mirrors manual scenarios A1–A6, A8 (SPEC §12).</summary>
    public class LaunchCoordinatorTests
    {
        private const uint A = 111;
        private const uint B = 222;
        private const int RunningPid = 4242;

        private readonly FakeSteamEnvironment env = new FakeSteamEnvironment();
        private readonly FakeClock clock = new FakeClock();
        private readonly SteamSimulator steam;
        private readonly FakeInteraction interaction = new FakeInteraction();
        private readonly List<SteamAccount> accounts = new List<SteamAccount>
        {
            new SteamAccount(SteamIdConverter.ToSteamId64(A), "a_login", "Alpha", true, false),
            new SteamAccount(SteamIdConverter.ToSteamId64(B), "b_login", "Beta", true, true),
        };

        public LaunchCoordinatorTests()
        {
            steam = new SteamSimulator(env, clock);
            steam.RememberedAccounts["a_login"] = A;
            steam.RememberedAccounts["b_login"] = B;
        }

        private LaunchOutcome Prepare(string target, bool askBeforeSwitch = false, string playniteGame = null, bool steamInstalled = true, CancellationToken token = default(CancellationToken))
        {
            var switcher = steamInstalled
                ? new SteamSwitcher(env, clock, SteamSimulator.Installation, new SwitchOptions { LoginTimeout = TimeSpan.FromSeconds(30) })
                : null;
            var coordinator = new LaunchCoordinator(env, switcher, interaction);
            return coordinator.Prepare(
                new LaunchRequest { TargetAccountName = target, AskBeforeSwitch = askBeforeSwitch, PlayniteRunningSteamGame = playniteGame },
                accounts,
                null,
                token);
        }

        [Fact]
        public void A1_steam_closed_starts_on_target()
        {
            var outcome = Prepare("a_login");

            Assert.Equal(LaunchOutcomeKind.Proceed, outcome.Kind);
            Assert.Equal(A, SteamStateReader.Read(env).ActiveUser);
            Assert.Empty(interaction.Questions);
        }

        [Fact]
        public void A2_already_on_target_touches_nothing()
        {
            env.SetSteamRunning(RunningPid, A);

            var outcome = Prepare("A_LOGIN");

            Assert.Equal(LaunchOutcomeKind.Proceed, outcome.Kind);
            Assert.Empty(env.Log);
        }

        [Fact]
        public void A3_other_account_switches()
        {
            env.SetSteamRunning(RunningPid, B);

            var outcome = Prepare("a_login");

            Assert.Equal(LaunchOutcomeKind.Proceed, outcome.Kind);
            Assert.Contains($"start: {SteamSimulator.ExePath} -shutdown", env.Log);
            Assert.Equal(A, SteamStateReader.Read(env).ActiveUser);
        }

        [Fact]
        public void A4_game_running_and_user_declines_leaves_steam_untouched()
        {
            env.SetSteamRunning(RunningPid, B, runningAppId: 570);
            env.SetUserValue(@"Apps\570", "Name", "Dota 2");
            interaction.Answer = false;

            var outcome = Prepare("a_login");

            Assert.Equal(LaunchOutcomeKind.Cancelled, outcome.Kind);
            Assert.Empty(env.Log);
            Assert.Equal("GameRunning a_login<-b_login Dota 2", Assert.Single(interaction.Questions));
        }

        [Fact]
        public void A4_game_running_and_user_accepts_switches()
        {
            env.SetSteamRunning(RunningPid, B, runningAppId: 570);

            var outcome = Prepare("a_login");

            Assert.Equal(LaunchOutcomeKind.Proceed, outcome.Kind);
            Assert.Equal(A, SteamStateReader.Read(env).ActiveUser);
        }

        [Fact]
        public void A5_account_without_remembered_password_fails_with_login_timeout()
        {
            steam.RememberedAccounts.Remove("a_login");

            var outcome = Prepare("a_login");

            Assert.Equal(LaunchOutcomeKind.Failed, outcome.Kind);
            Assert.Equal(LaunchFailure.LoginTimeout, outcome.Failure);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void A6_no_assignment_does_nothing(string target)
        {
            env.SetSteamRunning(RunningPid, B);

            Assert.Equal(LaunchOutcomeKind.Proceed, Prepare(target).Kind);
            Assert.Empty(env.Log);
        }

        [Fact]
        public void Ask_before_switch_setting_asks_and_respects_no()
        {
            env.SetSteamRunning(RunningPid, B);
            interaction.Answer = false;

            var outcome = Prepare("a_login", askBeforeSwitch: true);

            Assert.Equal(LaunchOutcomeKind.Cancelled, outcome.Kind);
            Assert.StartsWith("AskBeforeSwitch", Assert.Single(interaction.Questions));
        }

        [Fact]
        public void Playnite_tracked_game_requires_confirmation()
        {
            env.SetSteamRunning(RunningPid, B);
            interaction.Answer = false;

            Prepare("a_login", playniteGame: "Portal 2");

            Assert.EndsWith("Portal 2", Assert.Single(interaction.Questions));
        }

        [Fact]
        public void Logging_in_on_target_waits_then_proceeds_without_restart()
        {
            env.SetSteamRunning(RunningPid, 0);
            clock.At(TimeSpan.FromSeconds(5), () => env.SetUserValue("ActiveProcess", "ActiveUser", A));

            var outcome = Prepare("a_login");

            Assert.Equal(LaunchOutcomeKind.Proceed, outcome.Kind);
            Assert.Empty(env.Log);
        }

        [Fact]
        public void Logging_in_on_other_account_waits_then_switches()
        {
            env.SetSteamRunning(RunningPid, 0);
            clock.At(TimeSpan.FromSeconds(5), () => env.SetUserValue("ActiveProcess", "ActiveUser", B));

            var outcome = Prepare("a_login");

            Assert.Equal(LaunchOutcomeKind.Proceed, outcome.Kind);
            Assert.Equal(A, SteamStateReader.Read(env).ActiveUser);
        }

        [Fact]
        public void Logging_in_forever_fails_with_login_timeout()
        {
            env.SetSteamRunning(RunningPid, 0);

            var outcome = Prepare("a_login");

            Assert.Equal(LaunchFailure.LoginTimeout, outcome.Failure);
            Assert.Empty(env.Log);
        }

        [Fact]
        public void Unknown_account_fails()
        {
            Assert.Equal(LaunchFailure.UnknownAccount, Prepare("ghost").Failure);
        }

        [Fact]
        public void Missing_steam_fails_only_when_switch_is_needed()
        {
            Assert.Equal(LaunchFailure.SteamNotFound, Prepare("a_login", steamInstalled: false).Failure);
        }

        [Fact]
        public void Shutdown_timeout_is_reported()
        {
            env.SetSteamRunning(RunningPid, B);
            steam.HangOnShutdown = true;

            Assert.Equal(LaunchFailure.ShutdownTimeout, Prepare("a_login").Failure);
        }

        [Fact]
        public void Cancellation_maps_to_cancelled()
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                Assert.Equal(LaunchOutcomeKind.Cancelled, Prepare("a_login", token: cts.Token).Kind);
            }
        }

        private sealed class FakeInteraction : ILaunchInteraction
        {
            public bool Answer { get; set; } = true;

            public List<string> Questions { get; } = new List<string>();

            public bool ConfirmSwitch(SwitchConfirmation kind, SteamAccount target, SteamAccount current, string runningGameName)
            {
                Questions.Add($"{kind} {target.AccountName}<-{current?.AccountName} {runningGameName}".TrimEnd());
                return Answer;
            }
        }
    }
}
