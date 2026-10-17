using System;
using System.Collections.Generic;
using SteamAccountSwitcher.Core.Steam;

namespace SteamAccountSwitcher.Settings
{
    /// <summary>Persisted plugin settings (SPEC §7.2).</summary>
    public class SteamAccountSwitcherSettings : ObservableObject
    {
        public const int MinTimeoutSeconds = 5;
        public const int MaxTimeoutSeconds = 600;

        private string steamPath = string.Empty;
        private string defaultAccount = string.Empty;
        private bool askBeforeSwitch;
        private string launchArguments = "-silent";
        private int shutdownTimeoutSeconds = 60;
        private int loginTimeoutSeconds = 60;

        /// <summary>Steam directory; empty = auto-detect.</summary>
        public string SteamPath { get => steamPath; set => SetValue(ref steamPath, value); }

        /// <summary>AccountName used for games without an assignment; empty = do nothing.</summary>
        public string DefaultAccount { get => defaultAccount; set => SetValue(ref defaultAccount, value); }

        public bool AskBeforeSwitch { get => askBeforeSwitch; set => SetValue(ref askBeforeSwitch, value); }

        public string LaunchArguments { get => launchArguments; set => SetValue(ref launchArguments, value); }

        public int ShutdownTimeoutSeconds { get => shutdownTimeoutSeconds; set => SetValue(ref shutdownTimeoutSeconds, value); }

        public int LoginTimeoutSeconds { get => loginTimeoutSeconds; set => SetValue(ref loginTimeoutSeconds, value); }

        public SwitchOptions ToSwitchOptions() => new SwitchOptions
        {
            LaunchArguments = LaunchArguments ?? string.Empty,
            ShutdownTimeout = TimeSpan.FromSeconds(ShutdownTimeoutSeconds),
            LoginTimeout = TimeSpan.FromSeconds(LoginTimeoutSeconds),
        };

        /// <summary>Returns localization keys of validation errors.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (ShutdownTimeoutSeconds < MinTimeoutSeconds || ShutdownTimeoutSeconds > MaxTimeoutSeconds
                || LoginTimeoutSeconds < MinTimeoutSeconds || LoginTimeoutSeconds > MaxTimeoutSeconds)
            {
                errors.Add("LOCSAS_ValidationTimeout");
            }

            // Never pass credentials to Steam (hard project rule): switching works via AutoLoginUser only.
            if ((LaunchArguments ?? string.Empty).IndexOf("-login", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                errors.Add("LOCSAS_ValidationLoginArgument");
            }

            return errors;
        }
    }
}
