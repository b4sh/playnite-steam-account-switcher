using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SteamAccountSwitcher.Core.Steam;

namespace SteamAccountSwitcher.Core.Decision
{
    /// <summary>User questions asked while preparing a launch; implemented by the plugin with dialogs.</summary>
    public interface ILaunchInteraction
    {
        /// <summary>Returns false to cancel the game start.</summary>
        bool ConfirmSwitch(SwitchConfirmation kind, SteamAccount target, SteamAccount current, string runningGameName);

        /// <summary>A wait timed out; returns true to wait another timeout period, false to cancel the game start.</summary>
        bool ConfirmKeepWaiting(SwitchWait wait, SteamAccount target);
    }

    public enum LaunchOutcomeKind
    {
        /// <summary>Steam is on the right account (or nothing to do): let the game start.</summary>
        Proceed,

        /// <summary>The user declined or cancelled: cancel the game start silently.</summary>
        Cancelled,

        /// <summary>Cancel the game start and show an error.</summary>
        Failed,
    }

    public enum LaunchFailure
    {
        None,
        SteamNotFound,
        UnknownAccount,
        ShutdownTimeout,
        LoginTimeout,
        WrongAccount,
    }

    public sealed class LaunchOutcome
    {
        private LaunchOutcome(LaunchOutcomeKind kind, LaunchFailure failure, string details)
        {
            Kind = kind;
            Failure = failure;
            Details = details;
        }

        public LaunchOutcomeKind Kind { get; }

        public LaunchFailure Failure { get; }

        /// <summary>Technical details for the log.</summary>
        public string Details { get; }

        public static LaunchOutcome Proceed(string details) => new LaunchOutcome(LaunchOutcomeKind.Proceed, LaunchFailure.None, details);

        public static LaunchOutcome Cancelled(string details) => new LaunchOutcome(LaunchOutcomeKind.Cancelled, LaunchFailure.None, details);

        public static LaunchOutcome Failed(LaunchFailure failure, string details) => new LaunchOutcome(LaunchOutcomeKind.Failed, failure, details);

        public override string ToString() => Failure == LaunchFailure.None ? $"{Kind}: {Details}" : $"{Kind} ({Failure}): {Details}";
    }

    public sealed class LaunchRequest
    {
        /// <summary>Assigned or default account name; null/empty = no assignment.</summary>
        public string TargetAccountName { get; set; }

        public bool AskBeforeSwitch { get; set; }

        /// <summary>Another Steam game Playnite tracks as running; null = none.</summary>
        public string PlayniteRunningSteamGame { get; set; }
    }

    /// <summary>Carries out <see cref="LaunchDecider"/> decisions before a game starts (SPEC §6).</summary>
    public sealed class LaunchCoordinator
    {
        private readonly ISteamEnvironment environment;
        private readonly SteamSwitcher switcher;
        private readonly ILaunchInteraction interaction;

        public LaunchCoordinator(ISteamEnvironment environment, SteamSwitcher switcher, ILaunchInteraction interaction)
        {
            this.environment = environment ?? throw new ArgumentNullException(nameof(environment));
            this.switcher = switcher;
            this.interaction = interaction ?? throw new ArgumentNullException(nameof(interaction));
        }

        /// <param name="switcher">Null when Steam was not found; only matters if a switch is needed.</param>
        public LaunchOutcome Prepare(LaunchRequest request, IReadOnlyList<SteamAccount> accounts, IProgress<SwitchStage> progress, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(request.TargetAccountName))
            {
                return LaunchOutcome.Proceed("no account assigned");
            }

            var target = accounts.FirstOrDefault(a => string.Equals(a.AccountName, request.TargetAccountName, StringComparison.OrdinalIgnoreCase));
            var waitedForLogin = false;

            // "Stop waiting" is the user's choice: cancel silently instead of reporting a timeout error.
            var userStoppedWaiting = false;
            Func<SwitchWait, bool> keepWaiting = wait =>
            {
                var keep = interaction.ConfirmKeepWaiting(wait, target);
                userStoppedWaiting = !keep;
                return keep;
            };
            try
            {
                while (true)
                {
                    var state = SteamStateReader.Read(environment);
                    var decision = LaunchDecider.Decide(new LaunchContext
                    {
                        Steam = state,
                        TargetAccountName = request.TargetAccountName,
                        TargetAccountId = target?.AccountId,
                        AskBeforeSwitch = request.AskBeforeSwitch,
                        PlayniteRunningSteamGame = request.PlayniteRunningSteamGame,
                    });

                    switch (decision.Action)
                    {
                        case LaunchAction.None:
                            return LaunchOutcome.Proceed($"Steam already on '{request.TargetAccountName}' ({state})");

                        case LaunchAction.UnknownAccount:
                            return LaunchOutcome.Failed(LaunchFailure.UnknownAccount, $"'{request.TargetAccountName}' is not in loginusers.vdf");

                        case LaunchAction.WaitForLogin:
                            if (waitedForLogin)
                            {
                                return LaunchOutcome.Failed(LaunchFailure.LoginTimeout, "Steam is running but nobody logged in");
                            }

                            if (switcher == null)
                            {
                                return LaunchOutcome.Failed(LaunchFailure.SteamNotFound, "Steam installation not found");
                            }

                            waitedForLogin = true;
                            progress?.Report(SwitchStage.WaitingForLogin);
                            switcher.WaitForAnyLogin(cancellationToken, keepWaiting);
                            if (userStoppedWaiting)
                            {
                                return LaunchOutcome.Cancelled("user stopped waiting for Steam login");
                            }

                            continue;

                        case LaunchAction.StartSteam:
                        case LaunchAction.SwitchAccount:
                            if (switcher == null)
                            {
                                return LaunchOutcome.Failed(LaunchFailure.SteamNotFound, "Steam installation not found");
                            }

                            if (decision.Confirmation != SwitchConfirmation.None)
                            {
                                var current = accounts.FirstOrDefault(a => a.AccountId == state.ActiveUser);
                                if (!interaction.ConfirmSwitch(decision.Confirmation, target, current, decision.RunningGameName))
                                {
                                    return LaunchOutcome.Cancelled($"user declined switch ({decision.Confirmation})");
                                }
                            }

                            switcher.SwitchTo(target, state, progress, cancellationToken, keepWaiting);
                            return LaunchOutcome.Proceed($"{decision.Action} to '{target.AccountName}' done");

                        default:
                            throw new InvalidOperationException("Unknown action " + decision.Action);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return LaunchOutcome.Cancelled("cancelled by user");
            }
            catch (SteamSwitchException ex) when (userStoppedWaiting)
            {
                return LaunchOutcome.Cancelled("user stopped waiting: " + ex.Message);
            }
            catch (SteamSwitchException ex)
            {
                return LaunchOutcome.Failed(ToFailure(ex.Error), ex.Message);
            }
        }

        private static LaunchFailure ToFailure(SteamSwitchError error)
        {
            switch (error)
            {
                case SteamSwitchError.ShutdownTimeout: return LaunchFailure.ShutdownTimeout;
                case SteamSwitchError.WrongAccount: return LaunchFailure.WrongAccount;
                default: return LaunchFailure.LoginTimeout;
            }
        }
    }
}
