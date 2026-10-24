using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using SteamAccountSwitcher.Core;
using SteamAccountSwitcher.Core.Decision;
using SteamAccountSwitcher.Core.Steam;
using SteamAccountSwitcher.Core.Storage;
using SteamAccountSwitcher.Settings;

namespace SteamAccountSwitcher
{
    /// <summary>Thin Playnite layer: all Steam logic lives in SteamAccountSwitcher.Core (SPEC §4).</summary>
    public class SteamAccountSwitcherPlugin : GenericPlugin
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        /// <summary>Playnite's built-in Steam library plugin.</summary>
        private static readonly Guid SteamLibraryPluginId = Guid.Parse("CB91DFC9-B977-43BF-8E70-55F46E410FAB");

        private readonly ISteamEnvironment environment = new WindowsSteamEnvironment();
        private readonly GameAccountStore store;
        private readonly SteamAccountSwitcherSettingsViewModel settings;
        private TopPanel topPanel;

        /// <summary>1 while a game-start or manual switch runs; prevents parallel switches.</summary>
        private int switching;

        public SteamAccountSwitcherPlugin(IPlayniteAPI api)
            : base(api)
        {
            Properties = new GenericPluginProperties { HasSettings = true };
            store = new GameAccountStore(environment, Path.Combine(GetPluginUserDataPath(), "accounts.json"));
            Accounts = new AccountCache(environment);
            settings = new SteamAccountSwitcherSettingsViewModel(this);
        }

        public override Guid Id { get; } = Guid.Parse("386277e5-4f8d-4979-bf1f-09b635195a7b");

        internal AccountCache Accounts { get; }

        internal bool IsSwitching => switching != 0;

        internal SteamInstallation LocateSteam() => SteamLocator.Locate(environment, settings.Settings.SteamPath);

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            Accounts.Initialize(LocateSteam());
            LoadStore();

            var removed = store.RemoveMissing(new HashSet<Guid>(PlayniteApi.Database.Games.Select(g => g.Id)));
            if (removed > 0)
            {
                Logger.Info($"Removed {removed} assignment(s) of games no longer in the library.");
                SaveStore();
            }
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            topPanel?.Stop();
            Accounts.Dispose();
        }

        public override IEnumerable<TopPanelItem> GetTopPanelItems()
        {
            // Called on the UI thread; the item (and its WPF content) is created once.
            if (topPanel == null)
            {
                topPanel = new TopPanel(this, environment);
            }

            yield return topPanel.Item;
        }

        public override ISettings GetSettings(bool firstRunSettings) => settings;

        public override UserControl GetSettingsView(bool firstRunSettings) => new SteamAccountSwitcherSettingsView();

        /// <summary>Called by the settings view model after the user saved settings.</summary>
        internal void OnSettingsSaved()
        {
            Accounts.Initialize(LocateSteam());
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            // Called on every menu open: memory only, no I/O (SPEC §7.1).
            if (args.Games == null || args.Games.Count == 0)
            {
                yield break;
            }

            var section = ResourceProvider.GetString("LOCSAS_MenuSection");
            var gameIds = args.Games.Select(g => g.Id).ToList();
            var assigned = gameIds.Count == 1 ? store.Get(gameIds[0]) : null;
            var accounts = Accounts.Accounts;

            if (accounts.Count == 0)
            {
                yield return new GameMenuItem
                {
                    MenuSection = section,
                    Description = ResourceProvider.GetString("LOCSAS_MenuNoAccounts"),
                    Action = _ => PlayniteApi.Dialogs.ShowMessage(ResourceProvider.GetString("LOCSAS_MenuNoAccountsDetails")),
                };
            }

            foreach (var account in accounts)
            {
                var name = account.AccountName;
                var description = EscapeAccessKeys(account.DisplayName);
                if (string.Equals(assigned, name, StringComparison.OrdinalIgnoreCase))
                {
                    description = "✓ " + description;
                }

                if (!account.RememberPassword)
                {
                    description += " – " + ResourceProvider.GetString("LOCSAS_NoRememberedPasswordShort");
                }

                yield return new GameMenuItem
                {
                    MenuSection = section,
                    Description = description,
                    Action = _ => Assign(gameIds, name),
                };
            }

            yield return new GameMenuItem
            {
                MenuSection = section,
                Description = LaunchTarget.IsAsk(assigned)
                    ? "✓ " + ResourceProvider.GetString("LOCSAS_MenuAskBeforeStart")
                    : ResourceProvider.GetString("LOCSAS_MenuAskBeforeStart"),
                Action = _ => Assign(gameIds, LaunchTarget.AskMarker),
            };

            yield return new GameMenuItem
            {
                MenuSection = section,
                Description = assigned == null && gameIds.Count == 1
                    ? "✓ " + ResourceProvider.GetString("LOCSAS_MenuUseDefault")
                    : ResourceProvider.GetString("LOCSAS_MenuUseDefault"),
                Action = _ => Assign(gameIds, null),
            };
        }

        /// <summary>
        /// Menu headers treat "_" as an access-key marker and hide the first one (e.g. "_login_" shows as "login_");
        /// a doubled "__" renders as a literal underscore.
        /// </summary>
        internal static string EscapeAccessKeys(string text) => text?.Replace("_", "__");

        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
            var game = args.Game;
            var target = LaunchTarget.Resolve(store.Get(game.Id), settings.Settings.DefaultAccount);
            if (target.Kind == LaunchTargetKind.None)
            {
                return;
            }

            var source = $"game='{game.Name}'";
            var askBeforeSwitch = settings.Settings.AskBeforeSwitch;
            var accountName = target.AccountName;
            if (target.Kind == LaunchTargetKind.Ask)
            {
                accountName = AskForAccount(game.Name, source);
                if (accountName == null)
                {
                    args.CancelStartup = true;
                    return;
                }

                // The user has just picked the account: don't ask "switch?" again.
                askBeforeSwitch = false;
            }

            var outcome = RunExclusive(accountName, game.Id, askBeforeSwitch, source);
            if (outcome.Kind != LaunchOutcomeKind.Proceed)
            {
                args.CancelStartup = true;
            }
        }

        /// <summary>Shows the account picker (SPEC §7.5); null = start cancelled.</summary>
        private string AskForAccount(string gameName, string source)
        {
            var steam = LocateSteam();
            var accounts = steam == null ? new SteamAccount[0] : LoginUsersFile.Load(environment, steam.LoginUsersPath);
            if (accounts.Count == 0)
            {
                Logger.Warn($"{source} ask: no Steam accounts found, start cancelled.");
                PlayniteApi.Dialogs.ShowErrorMessage(
                    steam == null ? ResourceProvider.GetString("LOCSAS_ErrorSteamNotFound") : ResourceProvider.GetString("LOCSAS_MenuNoAccountsDetails"),
                    ResourceProvider.GetString("LOCSAS_PluginName"));
                return null;
            }

            var state = SteamStateReader.Read(environment);
            var chosen = PlayniteApi.MainView.UIDispatcher.Invoke(() => AccountPicker.Show(PlayniteApi, gameName, accounts, state));
            Logger.Info(chosen == null ? $"{source} ask: cancelled by the user." : $"{source} ask: chose '{chosen.AccountName}'.");
            return chosen?.AccountName;
        }

        /// <summary>Manual switch from the top panel (SPEC §7.4); same flow as a game start, without asking first.</summary>
        internal void SwitchManually(string accountName)
        {
            RunExclusive(accountName, null, false, "manual");
        }

        /// <summary>
        /// Runs one switch at a time, logs the outcome and shows errors. Returns Cancelled when another
        /// switch is already running.
        /// </summary>
        private LaunchOutcome RunExclusive(string target, Guid? gameId, bool askBeforeSwitch, string source)
        {
            if (System.Threading.Interlocked.CompareExchange(ref switching, 1, 0) != 0)
            {
                Logger.Warn($"{source} target='{target}': another Steam switch is in progress.");
                PlayniteApi.Dialogs.ShowMessage(ResourceProvider.GetString("LOCSAS_TopPanelSwitching"), ResourceProvider.GetString("LOCSAS_PluginName"));
                return LaunchOutcome.Cancelled("another switch in progress");
            }

            try
            {
                var outcome = PrepareSteam(gameId, target, askBeforeSwitch);
                Logger.Info($"{source} target='{target}' outcome={outcome}");
                if (outcome.Kind == LaunchOutcomeKind.Failed)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(FailureMessage(outcome.Failure, target), ResourceProvider.GetString("LOCSAS_PluginName"));
                }

                return outcome;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"{source} target='{target}': switching Steam failed.");
                PlayniteApi.Dialogs.ShowErrorMessage(
                    string.Format(ResourceProvider.GetString("LOCSAS_ErrorUnexpected"), ex.Message),
                    ResourceProvider.GetString("LOCSAS_PluginName"));
                return LaunchOutcome.Failed(LaunchFailure.None, ex.Message);
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref switching, 0);
                topPanel?.Refresh();
            }
        }

        /// <param name="gameId">The game being started (excluded from "running game" detection); null for a manual switch.</param>
        /// <param name="askBeforeSwitch">False when the user has just chosen the account explicitly (manual switch, picker).</param>
        private LaunchOutcome PrepareSteam(Guid? gameId, string target, bool askBeforeSwitch)
        {
            // Fresh account list for the decision; the menu cache may lag behind the watcher.
            var steam = LocateSteam();
            var accounts = steam == null ? new SteamAccount[0] : LoginUsersFile.Load(environment, steam.LoginUsersPath);
            var options = settings.Settings.ToSwitchOptions();
            var switcher = steam == null ? null : new SteamSwitcher(environment, SystemClock.Instance, steam, options) { Diagnostics = Logger.Info };
            var coordinator = new LaunchCoordinator(environment, switcher, new PlayniteLaunchInteraction(PlayniteApi));
            var request = new LaunchRequest
            {
                TargetAccountName = target,
                AskBeforeSwitch = askBeforeSwitch,
                PlayniteRunningSteamGame = PlayniteApi.Database.Games
                    .FirstOrDefault(g => g.Id != gameId && g.PluginId == SteamLibraryPluginId && (g.IsRunning || g.IsLaunching))?.Name,
            };

            // Fast path without a progress window when nothing needs to be done (A2, A8).
            var quick = LaunchDecider.Decide(new LaunchContext
            {
                Steam = SteamStateReader.Read(environment),
                TargetAccountName = target,
                TargetAccountId = accounts.FirstOrDefault(a => string.Equals(a.AccountName, target, StringComparison.OrdinalIgnoreCase))?.AccountId,
                AskBeforeSwitch = request.AskBeforeSwitch,
                PlayniteRunningSteamGame = request.PlayniteRunningSteamGame,
            });
            if (quick.Action == LaunchAction.None || quick.Action == LaunchAction.UnknownAccount)
            {
                return coordinator.Prepare(request, accounts, null, System.Threading.CancellationToken.None);
            }

            var targetName = accounts.FirstOrDefault(a => string.Equals(a.AccountName, target, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? target;
            LaunchOutcome outcome = null;
            var progressOptions = new GlobalProgressOptions(string.Format(ResourceProvider.GetString("LOCSAS_ProgressSwitching"), targetName), true)
            {
                IsIndeterminate = true,
            };
            var result = PlayniteApi.Dialogs.ActivateGlobalProgress(
                progressArgs =>
                {
                    var progress = new StageProgress(stage => progressArgs.Text = StageText(stage, targetName));
                    outcome = coordinator.Prepare(request, accounts, progress, progressArgs.CancelToken);
                },
                progressOptions);

            if (result?.Error != null)
            {
                throw new InvalidOperationException("Steam switch failed.", result.Error);
            }

            return outcome ?? LaunchOutcome.Cancelled("progress closed without result");
        }

        private static string StageText(SwitchStage stage, string targetName)
        {
            switch (stage)
            {
                case SwitchStage.ShuttingDown:
                    return ResourceProvider.GetString("LOCSAS_ProgressShuttingDown");
                case SwitchStage.Starting:
                    return string.Format(ResourceProvider.GetString("LOCSAS_ProgressStarting"), targetName);
                default:
                    return string.Format(ResourceProvider.GetString("LOCSAS_ProgressWaitingForLogin"), targetName);
            }
        }

        private string FailureMessage(LaunchFailure failure, string target)
        {
            switch (failure)
            {
                case LaunchFailure.SteamNotFound:
                    return ResourceProvider.GetString("LOCSAS_ErrorSteamNotFound");
                case LaunchFailure.UnknownAccount:
                    return string.Format(ResourceProvider.GetString("LOCSAS_ErrorUnknownAccount"), target);
                case LaunchFailure.ShutdownTimeout:
                    return string.Format(ResourceProvider.GetString("LOCSAS_ErrorShutdownTimeout"), settings.Settings.ShutdownTimeoutSeconds);
                case LaunchFailure.WrongAccount:
                    return string.Format(ResourceProvider.GetString("LOCSAS_ErrorWrongAccount"), target);
                default:
                    return string.Format(ResourceProvider.GetString("LOCSAS_ErrorLoginTimeout"), target);
            }
        }

        private void Assign(IList<Guid> gameIds, string accountName)
        {
            store.Assign(gameIds, accountName);
            Logger.Info($"Assigned account '{accountName ?? "(default)"}' to {gameIds.Count} game(s).");
            SaveStore();
        }

        private void LoadStore()
        {
            try
            {
                store.Load();
                Logger.Info($"Loaded {store.Count} game assignment(s).");
            }
            catch (InvalidDataException ex)
            {
                // Keep the broken file for the user instead of overwriting it on the next save.
                var path = Path.Combine(GetPluginUserDataPath(), "accounts.json");
                var backup = path + ".corrupt-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                Logger.Error(ex, $"accounts.json is invalid, moved to {backup}.");
                File.Move(path, backup);
                PlayniteApi.Notifications.Add(new NotificationMessage(
                    "LOCSAS_StoreCorrupt",
                    string.Format(ResourceProvider.GetString("LOCSAS_ErrorStoreCorrupt"), backup),
                    NotificationType.Error));
            }
        }

        private void SaveStore()
        {
            try
            {
                store.Save();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Logger.Error(ex, "Saving accounts.json failed.");
                PlayniteApi.Dialogs.ShowErrorMessage(
                    string.Format(ResourceProvider.GetString("LOCSAS_ErrorStoreSave"), ex.Message),
                    ResourceProvider.GetString("LOCSAS_PluginName"));
            }
        }

        private sealed class StageProgress : IProgress<SwitchStage>
        {
            private readonly Action<SwitchStage> report;

            public StageProgress(Action<SwitchStage> report) => this.report = report;

            public void Report(SwitchStage value) => report(value);
        }
    }
}
