using System;
using System.Threading;

namespace SteamAccountSwitcher.Core.Steam
{
    public sealed class SwitchOptions
    {
        public string LaunchArguments { get; set; } = "-silent";

        public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(60);

        public TimeSpan LoginTimeout { get; set; } = TimeSpan.FromSeconds(60);

        public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);
    }

    public enum SwitchStage
    {
        ShuttingDown,
        Starting,
        WaitingForLogin,
    }

    /// <summary>What a timed-out wait was waiting for; passed to the keep-waiting callback.</summary>
    public enum SwitchWait
    {
        /// <summary>steam.exe is still closing (usually Steam Cloud sync).</summary>
        Shutdown,

        /// <summary>Steam is still not logged in (usually waiting for a password or Steam Guard code).</summary>
        Login,
    }

    public enum SteamSwitchError
    {
        /// <summary>steam.exe did not exit within the shutdown timeout (e.g. a long Steam Cloud sync).</summary>
        ShutdownTimeout,

        /// <summary>No login within the timeout; Steam most likely asks for a password or Steam Guard code.</summary>
        LoginTimeout,

        /// <summary>Steam logged into a different account than requested.</summary>
        WrongAccount,
    }

    public sealed class SteamSwitchException : Exception
    {
        public SteamSwitchException(SteamSwitchError error, string message)
            : base(message)
        {
            Error = error;
        }

        public SteamSwitchError Error { get; }
    }

    /// <summary>
    /// Switches the Steam account via AutoLoginUser (SPEC §5.4): shutdown → registry → start → wait.
    /// Never kills Steam: it is only ever closed with "steam.exe -shutdown", which waits for Steam Cloud sync.
    /// Every wait takes an optional keepWaiting callback, asked when the timeout elapses: true restarts the
    /// timeout, false (or no callback) gives up (SPEC §10, M2).
    /// </summary>
    public sealed class SteamSwitcher
    {
        private readonly ISteamEnvironment environment;
        private readonly IClock clock;
        private readonly SteamInstallation installation;
        private readonly SwitchOptions options;

        public SteamSwitcher(ISteamEnvironment environment, IClock clock, SteamInstallation installation, SwitchOptions options)
        {
            this.environment = environment ?? throw new ArgumentNullException(nameof(environment));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.installation = installation ?? throw new ArgumentNullException(nameof(installation));
            this.options = options ?? new SwitchOptions();
        }

        /// <summary>Closes Steam if it runs, sets AutoLoginUser, starts Steam and waits until <paramref name="account"/> is logged in.</summary>
        public void SwitchTo(SteamAccount account, SteamState currentState, IProgress<SwitchStage> progress, CancellationToken cancellationToken, Func<SwitchWait, bool> keepWaiting = null)
        {
            if (currentState.IsRunning)
            {
                progress?.Report(SwitchStage.ShuttingDown);
                Shutdown(currentState.ProcessId, cancellationToken, keepWaiting);
            }

            progress?.Report(SwitchStage.Starting);
            environment.SetAutoLoginUser(account.AccountName);
            environment.StartProcess(installation.ExePath, options.LaunchArguments ?? string.Empty);

            progress?.Report(SwitchStage.WaitingForLogin);
            WaitForLogin(account.AccountId, cancellationToken, keepWaiting);
        }

        /// <summary>
        /// Sends -shutdown and waits until the given steam.exe process exits. Steam zeroes ActiveProcess\pid
        /// about a second before the process ends (M0), so the registry is not a reliable exit signal.
        /// </summary>
        public void Shutdown(int processId, CancellationToken cancellationToken, Func<SwitchWait, bool> keepWaiting = null)
        {
            environment.StartProcess(installation.ExePath, "-shutdown");
            var deadline = clock.UtcNow + options.ShutdownTimeout;
            while (SteamStateReader.IsSteamProcess(environment, processId))
            {
                if (clock.UtcNow >= deadline && !KeepWaiting(keepWaiting, SwitchWait.Shutdown, options.ShutdownTimeout, ref deadline))
                {
                    throw new SteamSwitchException(SteamSwitchError.ShutdownTimeout,
                        $"Steam (pid {processId}) did not exit within {options.ShutdownTimeout.TotalSeconds:0} s.");
                }

                clock.Sleep(options.PollInterval, cancellationToken);
            }
        }

        /// <summary>Waits until Steam runs with ActiveUser == <paramref name="accountId"/>.</summary>
        public void WaitForLogin(uint accountId, CancellationToken cancellationToken, Func<SwitchWait, bool> keepWaiting = null)
        {
            var deadline = clock.UtcNow + options.LoginTimeout;
            while (true)
            {
                var state = SteamStateReader.Read(environment);
                if (state.IsRunning && state.ActiveUser == accountId)
                {
                    return;
                }

                if (state.IsRunning && state.ActiveUser != 0)
                {
                    throw new SteamSwitchException(SteamSwitchError.WrongAccount,
                        $"Steam logged into account {state.ActiveUser} instead of {accountId}.");
                }

                if (clock.UtcNow >= deadline && !KeepWaiting(keepWaiting, SwitchWait.Login, options.LoginTimeout, ref deadline))
                {
                    throw new SteamSwitchException(SteamSwitchError.LoginTimeout,
                        $"Account {accountId} was not logged in within {options.LoginTimeout.TotalSeconds:0} s.");
                }

                clock.Sleep(options.PollInterval, cancellationToken);
            }
        }

        /// <summary>
        /// Steam is starting (ActiveUser = 0): waits until some account is logged in or Steam exits,
        /// at most the login timeout. Returns the last state so the caller can decide again (SPEC §6).
        /// </summary>
        public SteamState WaitForAnyLogin(CancellationToken cancellationToken, Func<SwitchWait, bool> keepWaiting = null)
        {
            var deadline = clock.UtcNow + options.LoginTimeout;
            while (true)
            {
                var state = SteamStateReader.Read(environment);
                if (!state.IsRunning || state.ActiveUser != 0)
                {
                    return state;
                }

                if (clock.UtcNow >= deadline && !KeepWaiting(keepWaiting, SwitchWait.Login, options.LoginTimeout, ref deadline))
                {
                    return state;
                }

                clock.Sleep(options.PollInterval, cancellationToken);
            }
        }

        /// <summary>Asks whether to keep waiting; on yes the timeout starts over from now (the question may take long).</summary>
        private bool KeepWaiting(Func<SwitchWait, bool> keepWaiting, SwitchWait wait, TimeSpan timeout, ref DateTime deadline)
        {
            if (keepWaiting == null || !keepWaiting(wait))
            {
                return false;
            }

            deadline = clock.UtcNow + timeout;
            return true;
        }
    }
}
