using System.IO;

namespace SteamAccountSwitcher.Core.Steam
{
    public sealed class SteamInstallation
    {
        public SteamInstallation(string rootPath, string exePath)
        {
            RootPath = rootPath;
            ExePath = exePath;
        }

        public string RootPath { get; }

        public string ExePath { get; }

        public string LoginUsersPath => Path.Combine(RootPath, "config", "loginusers.vdf");
    }

    /// <summary>Finds the Steam installation (SPEC §5.1).</summary>
    public static class SteamLocator
    {
        /// <param name="overridePath">Steam directory from the settings; empty = auto-detect.</param>
        /// <returns>Null when Steam cannot be found.</returns>
        public static SteamInstallation Locate(ISteamEnvironment environment, string overridePath)
        {
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                var root = Normalize(overridePath);
                return new SteamInstallation(root, Path.Combine(root, "steam.exe"));
            }

            var steamPath = environment.GetUserSteamString(null, "SteamPath");
            if (!string.IsNullOrWhiteSpace(steamPath))
            {
                var root = Normalize(steamPath);
                var exe = environment.GetUserSteamString(null, "SteamExe");
                return new SteamInstallation(root, string.IsNullOrWhiteSpace(exe) ? Path.Combine(root, "steam.exe") : Normalize(exe));
            }

            var installPath = environment.GetMachineSteamInstallPath();
            if (!string.IsNullOrWhiteSpace(installPath))
            {
                var root = Normalize(installPath);
                return new SteamInstallation(root, Path.Combine(root, "steam.exe"));
            }

            return null;
        }

        /// <summary>The registry stores e.g. "c:/program files (x86)/steam".</summary>
        internal static string Normalize(string path) => path.Trim().Replace('/', '\\').TrimEnd('\\');
    }
}
