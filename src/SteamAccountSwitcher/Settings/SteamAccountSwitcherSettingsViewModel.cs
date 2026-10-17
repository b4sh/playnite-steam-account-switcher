using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace SteamAccountSwitcher.Settings
{
    public sealed class AccountOption
    {
        public AccountOption(string accountName, string displayName, string warning)
        {
            AccountName = accountName;
            DisplayName = displayName;
            Warning = warning;
        }

        /// <summary>Empty for the "no default account" entry.</summary>
        public string AccountName { get; }

        public string DisplayName { get; }

        /// <summary>Shown next to accounts Steam cannot log into without a password; null = none.</summary>
        public string Warning { get; }
    }

    public class SteamAccountSwitcherSettingsViewModel : ObservableObject, ISettings
    {
        private readonly SteamAccountSwitcherPlugin plugin;
        private SteamAccountSwitcherSettings editingClone;
        private SteamAccountSwitcherSettings settings;

        public SteamAccountSwitcherSettingsViewModel(SteamAccountSwitcherPlugin plugin)
        {
            this.plugin = plugin;
            Settings = plugin.LoadPluginSettings<SteamAccountSwitcherSettings>() ?? new SteamAccountSwitcherSettings();
            RefreshAccountsCommand = new RelayCommand(RefreshAccounts);
        }

        public SteamAccountSwitcherSettings Settings
        {
            get => settings;
            set => SetValue(ref settings, value);
        }

        /// <summary>Default-account choices: "none" plus every account.</summary>
        public ObservableCollection<AccountOption> DefaultAccountOptions { get; } = new ObservableCollection<AccountOption>();

        /// <summary>Detected accounts with warnings (SPEC §5.2, §7.2).</summary>
        public ObservableCollection<AccountOption> DetectedAccounts { get; } = new ObservableCollection<AccountOption>();

        public string DetectedSteamPath { get; private set; }

        public RelayCommand RefreshAccountsCommand { get; }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
            RefreshAccounts();
        }

        public void CancelEdit()
        {
            Settings = editingClone;

            // RefreshAccounts may have pointed the cache at an unsaved Steam path.
            plugin.OnSettingsSaved();
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(Settings);
            plugin.OnSettingsSaved();
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = Settings.Validate().Select(ResourceProvider.GetString).ToList();
            return errors.Count == 0;
        }

        private void RefreshAccounts()
        {
            var steam = plugin.LocateSteam();
            DetectedSteamPath = steam?.RootPath ?? ResourceProvider.GetString("LOCSAS_SettingsSteamNotFound");
            OnPropertyChanged(nameof(DetectedSteamPath));

            plugin.Accounts.Initialize(steam);
            var noPassword = ResourceProvider.GetString("LOCSAS_NoRememberedPassword");
            var accounts = plugin.Accounts.Accounts
                .Select(a => new AccountOption(a.AccountName, a.DisplayName, a.RememberPassword ? null : noPassword))
                .ToList();

            // Keep the current selection even if the account is gone, so the user sees it.
            var selected = Settings.DefaultAccount;
            DefaultAccountOptions.Clear();
            DefaultAccountOptions.Add(new AccountOption(string.Empty, ResourceProvider.GetString("LOCSAS_SettingsNoDefaultAccount"), null));
            foreach (var account in accounts)
            {
                DefaultAccountOptions.Add(account);
            }

            if (!string.IsNullOrEmpty(selected) && accounts.All(a => a.AccountName != selected))
            {
                DefaultAccountOptions.Add(new AccountOption(selected, selected, ResourceProvider.GetString("LOCSAS_AccountMissing")));
            }

            DetectedAccounts.Clear();
            foreach (var account in accounts)
            {
                DetectedAccounts.Add(account);
            }

            Settings.DefaultAccount = selected;
        }
    }
}
