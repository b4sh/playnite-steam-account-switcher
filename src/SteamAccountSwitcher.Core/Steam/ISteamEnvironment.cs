using System;

namespace SteamAccountSwitcher.Core.Steam
{
    /// <summary>
    /// The only way Core touches the registry, processes and files (SPEC §4).
    /// Keeps the write surface minimal: the only registry write is <see cref="SetAutoLoginUser"/>,
    /// and there is deliberately no way to kill a process (SPEC §5.4).
    /// </summary>
    public interface ISteamEnvironment
    {
        /// <summary>REG_SZ under HKCU\Software\Valve\Steam[\<paramref name="subKey"/>]; null when missing.</summary>
        string GetUserSteamString(string subKey, string name);

        /// <summary>DWORD under HKCU\Software\Valve\Steam[\<paramref name="subKey"/>]; null when missing.</summary>
        uint? GetUserSteamDword(string subKey, string name);

        /// <summary>HKLM (32-bit view) SOFTWARE\Valve\Steam\InstallPath; null when missing.</summary>
        string GetMachineSteamInstallPath();

        /// <summary>Writes AutoLoginUser = <paramref name="accountName"/> and RememberPassword = 1 under HKCU\Software\Valve\Steam.</summary>
        void SetAutoLoginUser(string accountName);

        /// <summary>Process name (without .exe) of a live process, or null when no such process exists.</summary>
        string GetProcessName(int processId);

        /// <summary>Starts a process and returns its id.</summary>
        int StartProcess(string fileName, string arguments);

        bool FileExists(string path);

        DateTime GetLastWriteTimeUtc(string path);

        string ReadAllText(string path);

        /// <summary>Writes via a temporary file and a replace, so readers never see a partial file.</summary>
        void WriteAllTextAtomic(string path, string contents);
    }
}
