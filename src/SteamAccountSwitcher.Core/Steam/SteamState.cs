namespace SteamAccountSwitcher.Core.Steam
{
    /// <summary>Snapshot of the Steam client state (SPEC §5.3).</summary>
    public sealed class SteamState
    {
        public static readonly SteamState NotRunning = new SteamState(false, 0, 0, 0, null);

        public SteamState(bool isRunning, int processId, uint activeUser, uint runningAppId, string runningAppName)
        {
            IsRunning = isRunning;
            ProcessId = processId;
            ActiveUser = activeUser;
            RunningAppId = runningAppId;
            RunningAppName = runningAppName;
        }

        /// <summary>ActiveProcess\pid points to a live steam.exe.</summary>
        public bool IsRunning { get; }

        public int ProcessId { get; }

        /// <summary>AccountId of the logged-in account; 0 = nobody logged in yet.</summary>
        public uint ActiveUser { get; }

        /// <summary>AppID of the running Steam game; 0 = none.</summary>
        public uint RunningAppId { get; }

        public string RunningAppName { get; }

        public override string ToString() =>
            IsRunning ? $"running (pid {ProcessId}, activeUser {ActiveUser}, runningAppId {RunningAppId})" : "not running";
    }
}
