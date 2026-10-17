using System;
using System.Collections.Generic;
using SteamAccountSwitcher.Core.Steam;

namespace SteamAccountSwitcher.Core.Tests.Fakes
{
    /// <summary>In-memory registry, process list and file system. Records every side effect in <see cref="Log"/>.</summary>
    public sealed class FakeSteamEnvironment : ISteamEnvironment
    {
        private readonly Dictionary<string, object> registry = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private int nextPid = 1000;

        public Dictionary<int, string> Processes { get; } = new Dictionary<int, string>();

        public Dictionary<string, string> Files { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string MachineInstallPath { get; set; }

        /// <summary>Side effects in order: "start: &lt;file&gt; &lt;args&gt;", "autologin: &lt;name&gt;", "write: &lt;path&gt;".</summary>
        public List<string> Log { get; } = new List<string>();

        /// <summary>Hook for simulating Steam's reaction to a started process; receives (file, args, pid).</summary>
        public Action<string, string, int> OnStartProcess { get; set; }

        public void SetUserValue(string subKey, string name, object value) => registry[Key(subKey, name)] = value;

        public object GetUserValue(string subKey, string name) => registry.TryGetValue(Key(subKey, name), out var v) ? v : null;

        public string GetUserSteamString(string subKey, string name) => GetUserValue(subKey, name) as string;

        public uint? GetUserSteamDword(string subKey, string name) => GetUserValue(subKey, name) as uint?;

        public string GetMachineSteamInstallPath() => MachineInstallPath;

        public void SetAutoLoginUser(string accountName)
        {
            SetUserValue(null, "AutoLoginUser", accountName);
            SetUserValue(null, "RememberPassword", 1u);
            Log.Add("autologin: " + accountName);
        }

        public string GetProcessName(int processId) => Processes.TryGetValue(processId, out var name) ? name : null;

        public int StartProcess(string fileName, string arguments)
        {
            var pid = nextPid++;
            Log.Add($"start: {fileName} {arguments}");
            OnStartProcess?.Invoke(fileName, arguments, pid);
            return pid;
        }

        public bool FileExists(string path) => Files.ContainsKey(path);

        public DateTime GetLastWriteTimeUtc(string path) => DateTime.MinValue;

        public string ReadAllText(string path) => Files[path];

        public void WriteAllTextAtomic(string path, string contents)
        {
            Files[path] = contents;
            Log.Add("write: " + path);
        }

        /// <summary>Steam running with the given pid and logged-in AccountId (0 = still logging in).</summary>
        public void SetSteamRunning(int pid, uint activeUser, uint runningAppId = 0)
        {
            Processes[pid] = "steam";
            SetUserValue("ActiveProcess", "pid", (uint)pid);
            SetUserValue("ActiveProcess", "ActiveUser", activeUser);
            SetUserValue(null, "RunningAppID", runningAppId);
        }

        private static string Key(string subKey, string name) => (subKey ?? string.Empty) + "|" + name;
    }
}
