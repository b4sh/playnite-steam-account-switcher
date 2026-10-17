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
        public void A5_no_remembered_password_user_stops_waiting_cancels_silently()
        {
            steam.RememberedAccounts.Remove("a_login");

            var outcome = Prepare("a_login");

            Assert.Equal(LaunchOutcomeKind.Cancelled, outcome.Kind);
            Assert.Equal(LaunchFailure.None, outcome.Failure);
            Assert.Equal("KeepWaiting Login a_login", Assert.Single(interaction.Questions));
        }

        [Fact]
        public void A5_user_logs_in_manually_after_keep_waiting_and_game_starts()
        {
            // Typing the password / Steam Guard code takes longer than the 30 s login timeout.
            steam.LoginDuration = TimeSpan.FromSeconds(50);
            interaction.KeepWaitingAnswers.Enqueue(true);

            var outcome = Prepare("a_login");

            Assert.Equal(LaunchOutcomeKind.Proceed, outcome.Kind);
            Assert.Equal(A, SteamStateReader.Read(env).ActiveUser);
            Assert.Equal("KeepWaiting Login a_login", Assert.Single(interaction.Questions));
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
        public void Logging_in_forever_asks_and_cancels_when_user_stops_waiting()
        {
            env.SetSteamRunning(RunningPid, 0);

            var outcome = Prepare("a_login");

            Assert.Equal(LaunchOutcomeKind.Cancelled, outcome.Kind);
            Assert.Equal("KeepWaiting Login a_login", Assert.Single(interaction.Questions));
            Assert.Empty(env.Log);
        }

        [Fact]
        public void Steam_on_login_screen_user_keeps_waiting_and_logs_in_on_target()
        {
            env.SetSteamRunning(RunningPid, 0);
            clock.At(TimeSpan.FromSeconds(45), () => env.SetUserValue("ActiveProcess", "ActiveUser", A));
            interaction.KeepWaitingAnswers.Enqueue(true);

            var outcome = Prepare("a_login");

            Assert.Equal(LaunchOutcomeKind.Proceed, outcome.Kind);
            Assert.Empty(env.Log);
        }

        [Fact]
        public void Wrong_account_is_an_error_not_a_question()
        {
            env.OnStartProcess = (file, args, pid) =>
            {
                env.SetSteamRunning(pid, 0);
                clock.At(TimeSpan.FromSeconds(3), () => env.SetUserValue("ActiveProcess", "ActiveUser", B));
            };

            var outcome = Prepare("a_login");

            Assert.Equal(LaunchFailure.WrongAccount, outcome.Failure);
            Assert.Empty(interaction.Questions);
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
        public void Shutdown_timeout_user_stops_waiting_cancels_and_leaves_steam_running()
        {
            env.SetSteamRunning(RunningPid, B);
            steam.HangOnShutdown = true;

            var outcome = Prepare("a_login");

            Assert.Equal(LaunchOutcomeKind.Cancelled, outcome.Kind);
            Assert.Equal("KeepWaiting Shutdown a_login", Assert.Single(interaction.Questions));
            Assert.True(env.Processes.ContainsKey(RunningPid));
        }

        [Fact]
        public void Long_cloud_sync_user_keeps_waiting_and_switch_completes()
        {
            env.SetSteamRunning(RunningPid, B);
            steam.ShutdownDuration = TimeSpan.FromSeconds(100);
            interaction.KeepWaitingAnswers.Enqueue(true);

            var outcome = Prepare("a_login");

            Assert.Equal(LaunchOutcomeKind.Proceed, outcome.Kind);
            Assert.Equal(A, SteamStateReader.Read(env).ActiveUser);
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

            /// <summary>Answers to "keep waiting?" in order; "no" once exhausted.</summary>
            public Queue<bool> KeepWaitingAnswers { get; } = new Queue<bool>();

            public List<string> Questions { get; } = new List<string>();

            public bool ConfirmSwitch(SwitchConfirmation kind, SteamAccount target, SteamAccount current, string runningGameName)
            {
                Questions.Add($"{kind} {target.AccountName}<-{current?.AccountName} {runningGameName}".TrimEnd());
                return Answer;
            }

            public bool ConfirmKeepWaiting(SwitchWait wait, SteamAccount target)
            {
                Questions.Add($"KeepWaiting {wait} {target?.AccountName}");
                return KeepWaitingAnswers.Count > 0 && KeepWaitingAnswers.Dequeue();
            }
        }
    }
}
