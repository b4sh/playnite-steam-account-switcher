using System;

namespace SteamAccountSwitcher.Core.Steam
{
    /// <summary>Reads <see cref="SteamState"/> from the registry and process list (SPEC §5.3).</summary>
    public static class SteamStateReader
    {
        internal const string ActiveProcessKey = "ActiveProcess";
        internal const string SteamProcessName = "steam";

        public static SteamState Read(ISteamEnvironment environment)
        {
            var pid = environment.GetUserSteamDword(ActiveProcessKey, "pid") ?? 0;

            // Steam zeroes pid on exit, but after a crash a stale value may remain: trust it only
            // when the process is alive and really is steam.exe.
            if (pid == 0 || pid > int.MaxValue || !IsSteamProcess(environment, (int)pid))
            {
                return SteamState.NotRunning;
            }

            var activeUser = environment.GetUserSteamDword(ActiveProcessKey, "ActiveUser") ?? 0;
            var runningAppId = environment.GetUserSteamDword(null, "RunningAppID") ?? 0;
            var runningAppName = runningAppId != 0 ? environment.GetUserSteamString("Apps\\" + runningAppId, "Name") : null;
            return new SteamState(true, (int)pid, activeUser, runningAppId, runningAppName);
        }

        internal static bool IsSteamProcess(ISteamEnvironment environment, int processId) =>
            string.Equals(environment.GetProcessName(processId), SteamProcessName, StringComparison.OrdinalIgnoreCase);
    }
}
