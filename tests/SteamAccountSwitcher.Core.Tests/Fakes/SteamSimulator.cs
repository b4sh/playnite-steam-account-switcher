using System;
using System.Collections.Generic;
using SteamAccountSwitcher.Core.Steam;

namespace SteamAccountSwitcher.Core.Tests.Fakes
{
    /// <summary>
    /// Reproduces the Steam client behaviour measured in M0 (SPEC §5.3) on top of the fakes:
    /// "-shutdown" zeroes pid/ActiveUser first and ends the process a second later;
    /// a normal start sets pid at once and ActiveUser a few seconds later, but only for an
    /// account with a remembered password.
    /// </summary>
    public sealed class SteamSimulator
    {
        public const string ExePath = @"C:\Steam\steam.exe";

        private readonly FakeSteamEnvironment env;
        private readonly FakeClock clock;

        public SteamSimulator(FakeSteamEnvironment env, FakeClock clock)
        {
            this.env = env;
            this.clock = clock;
            env.OnStartProcess = OnStart;
        }

        /// <summary>Login names (AccountName → AccountId) Steam can log into without a password.</summary>
        public Dictionary<string, uint> RememberedAccounts { get; } = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        public TimeSpan ShutdownDuration { get; set; } = TimeSpan.FromSeconds(2);

        public TimeSpan LoginDuration { get; set; } = TimeSpan.FromSeconds(7);

        /// <summary>When the steam.exe process actually exited (null = still running).</summary>
        public DateTime? ExitedAt { get; private set; }

        /// <summary>Simulates a Steam Cloud sync that never ends: -shutdown is accepted but the process stays.</summary>
        public bool HangOnShutdown { get; set; }

        public static SteamInstallation Installation => new SteamInstallation(@"C:\Steam", ExePath);

        private void OnStart(string file, string args, int pid)
        {
            if (!string.Equals(file, ExePath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (args == "-shutdown")
            {
                var running = (int)(env.GetUserSteamDword("ActiveProcess", "pid") ?? 0);
                if (running == 0 || HangOnShutdown)
                {
                    return;
                }

                clock.At(TimeSpan.FromTicks(ShutdownDuration.Ticks / 2), () =>
                {
                    env.SetUserValue("ActiveProcess", "pid", 0u);
                    env.SetUserValue("ActiveProcess", "ActiveUser", 0u);
                });
                clock.At(ShutdownDuration, () =>
                {
                    env.Processes.Remove(running);
                    ExitedAt = clock.UtcNow;
                });
                return;
            }

            env.Processes[pid] = "steam";
            env.SetUserValue("ActiveProcess", "pid", (uint)pid);
            env.SetUserValue("ActiveProcess", "ActiveUser", 0u);
            var user = env.GetUserSteamString(null, "AutoLoginUser");
            if (user != null && RememberedAccounts.TryGetValue(user, out var accountId))
            {
                clock.At(LoginDuration, () => env.SetUserValue("ActiveProcess", "ActiveUser", accountId));
            }
        }
    }
}
