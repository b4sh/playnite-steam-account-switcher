using System;
using System.Collections.Generic;
using System.Threading;
using SteamAccountSwitcher.Core.Steam;
using SteamAccountSwitcher.Core.Tests.Fakes;
using Xunit;

namespace SteamAccountSwitcher.Core.Tests
{
    public class SteamSwitcherTests
    {
        private const uint A = 111;
        private const uint B = 222;
        private const int RunningPid = 4242;

        private readonly FakeSteamEnvironment env = new FakeSteamEnvironment();
        private readonly FakeClock clock = new FakeClock();
        private readonly SteamSimulator steam;
        private readonly SteamAccount accountA = new SteamAccount(SteamIdConverter.ToSteamId64(A), "a_login", "Alpha", true, false);

        public SteamSwitcherTests()
        {
            steam = new SteamSimulator(env, clock);
            steam.RememberedAccounts["a_login"] = A;
            steam.RememberedAccounts["b_login"] = B;
        }

        private SteamSwitcher CreateSwitcher(SwitchOptions options = null) =>
            new SteamSwitcher(env, clock, SteamSimulator.Installation, options ?? new SwitchOptions());

        [Fact]
        public void Starts_closed_steam_on_target_account()
        {
            var stages = new List<SwitchStage>();

            CreateSwitcher().SwitchTo(accountA, SteamState.NotRunning, new SyncProgress<SwitchStage>(stages.Add), CancellationToken.None);

            Assert.Equal(new[] { "autologin: a_login", $"start: {SteamSimulator.ExePath} -silent" }, env.Log);
            Assert.Equal(new[] { SwitchStage.Starting, SwitchStage.WaitingForLogin }, stages);
            Assert.Equal(A, SteamStateReader.Read(env).ActiveUser);
        }

        [Fact]
        public void Switches_running_steam_via_shutdown_then_autologin()
        {
            env.SetSteamRunning(RunningPid, B);
            var stages = new List<SwitchStage>();

            CreateSwitcher().SwitchTo(accountA, SteamStateReader.Read(env), new SyncProgress<SwitchStage>(stages.Add), CancellationToken.None);

            Assert.Equal(
                new[] { $"start: {SteamSimulator.ExePath} -shutdown", "autologin: a_login", $"start: {SteamSimulator.ExePath} -silent" },
                env.Log);
            Assert.Equal(new[] { SwitchStage.ShuttingDown, SwitchStage.Starting, SwitchStage.WaitingForLogin }, stages);
            Assert.Equal(A, SteamStateReader.Read(env).ActiveUser);
        }

        [Fact]
        public void Shutdown_waits_for_process_exit_not_for_registry_pid_reset()
        {
            // M0: Steam zeroes ActiveProcess\pid about a second before steam.exe actually exits.
            env.SetSteamRunning(RunningPid, B);
            steam.ShutdownDuration = TimeSpan.FromSeconds(10);
            DateTime? autoLoginAt = null;
            env.OnStartProcess += (file, args, pid) =>
            {
                if (args == "-silent")
                {
                    autoLoginAt = clock.UtcNow;
                }
            };

            CreateSwitcher().SwitchTo(accountA, SteamStateReader.Read(env), null, CancellationToken.None);

            Assert.NotNull(steam.ExitedAt);
            Assert.True(autoLoginAt >= steam.ExitedAt, "Steam was started again before the old process exited.");
        }

        [Fact]
        public void Shutdown_timeout_fails_and_never_starts_steam_again()
        {
            env.SetSteamRunning(RunningPid, B);
            steam.HangOnShutdown = true;
            var options = new SwitchOptions { ShutdownTimeout = TimeSpan.FromSeconds(60) };

            var ex = Assert.Throws<SteamSwitchException>(() =>
                CreateSwitcher(options).SwitchTo(accountA, SteamStateReader.Read(env), null, CancellationToken.None));

            Assert.Equal(SteamSwitchError.ShutdownTimeout, ex.Error);
            Assert.Equal(new[] { $"start: {SteamSimulator.ExePath} -shutdown" }, env.Log);
            Assert.True(env.Processes.ContainsKey(RunningPid), "Steam must be left running, never killed.");
            Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(61));
        }

        [Fact]
        public void Login_timeout_when_account_needs_password()
        {
            steam.RememberedAccounts.Remove("a_login");
            var options = new SwitchOptions { LoginTimeout = TimeSpan.FromSeconds(30) };

            var ex = Assert.Throws<SteamSwitchException>(() =>
                CreateSwitcher(options).SwitchTo(accountA, SteamState.NotRunning, null, CancellationToken.None));

            Assert.Equal(SteamSwitchError.LoginTimeout, ex.Error);
            Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(31));
        }

        [Fact]
        public void Login_as_different_account_fails_immediately()
        {
            env.OnStartProcess = (file, args, pid) =>
            {
                env.SetSteamRunning(pid, 0);
                clock.At(TimeSpan.FromSeconds(3), () => env.SetUserValue("ActiveProcess", "ActiveUser", B));
            };

            var ex = Assert.Throws<SteamSwitchException>(() =>
                CreateSwitcher().SwitchTo(accountA, SteamState.NotRunning, null, CancellationToken.None));

            Assert.Equal(SteamSwitchError.WrongAccount, ex.Error);
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void Cancellation_stops_waiting()
        {
            steam.RememberedAccounts.Remove("a_login");
            using (var cts = new CancellationTokenSource())
            {
                clock.OnSleep = () =>
                {
                    if (clock.Elapsed >= TimeSpan.FromSeconds(5))
                    {
                        cts.Cancel();
                    }
                };

                Assert.ThrowsAny<OperationCanceledException>(() =>
                    CreateSwitcher().SwitchTo(accountA, SteamState.NotRunning, null, cts.Token));
            }

            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(6));
        }

        [Fact]
        public void WaitForAnyLogin_returns_once_someone_is_logged_in()
        {
            env.SetSteamRunning(RunningPid, 0);
            clock.At(TimeSpan.FromSeconds(4), () => env.SetUserValue("ActiveProcess", "ActiveUser", B));

            var state = CreateSwitcher().WaitForAnyLogin(CancellationToken.None);

            Assert.Equal(B, state.ActiveUser);
            Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void WaitForAnyLogin_returns_last_state_on_timeout()
        {
            env.SetSteamRunning(RunningPid, 0);

            var state = CreateSwitcher(new SwitchOptions { LoginTimeout = TimeSpan.FromSeconds(10) }).WaitForAnyLogin(CancellationToken.None);

            Assert.True(state.IsRunning);
            Assert.Equal(0u, state.ActiveUser);
            Assert.InRange(clock.Elapsed, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(11));
        }

        [Fact]
        public void WaitForAnyLogin_returns_when_steam_exits()
        {
            env.SetSteamRunning(RunningPid, 0);
            clock.At(TimeSpan.FromSeconds(2), () => env.Processes.Remove(RunningPid));

            Assert.False(CreateSwitcher().WaitForAnyLogin(CancellationToken.None).IsRunning);
        }

        /// <summary><see cref="Progress{T}"/> posts asynchronously; tests need the calls in order, synchronously.</summary>
        private sealed class SyncProgress<T> : IProgress<T>
        {
            private readonly Action<T> report;

            public SyncProgress(Action<T> report) => this.report = report;

            public void Report(T value) => report(value);
        }
    }
}
