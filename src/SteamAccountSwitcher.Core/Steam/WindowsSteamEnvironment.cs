using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace SteamAccountSwitcher.Core.Steam
{
    /// <summary>The real <see cref="ISteamEnvironment"/>; the only class in Core that touches Windows directly.</summary>
    public sealed class WindowsSteamEnvironment : ISteamEnvironment
    {
        private const string UserSteamKey = @"Software\Valve\Steam";
        private const string MachineSteamKey = @"SOFTWARE\Valve\Steam";

        public string GetUserSteamString(string subKey, string name) => ReadUserValue(subKey, name) as string;

        public uint? GetUserSteamDword(string subKey, string name)
        {
            // REG_DWORD comes back as a signed int.
            var value = ReadUserValue(subKey, name);
            return value is int i ? unchecked((uint)i) : (uint?)null;
        }

        public string GetMachineSteamInstallPath()
        {
            // Steam writes this from its 32-bit installer, i.e. under WOW6432Node on 64-bit Windows.
            using (var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
            using (var key = hklm.OpenSubKey(MachineSteamKey))
            {
                return key?.GetValue("InstallPath") as string;
            }
        }

        public void SetAutoLoginUser(string accountName)
        {
            if (string.IsNullOrEmpty(accountName))
            {
                throw new ArgumentException("Account name is required.", nameof(accountName));
            }

            using (var key = Registry.CurrentUser.CreateSubKey(UserSteamKey))
            {
                key.SetValue("AutoLoginUser", accountName, RegistryValueKind.String);
                key.SetValue("RememberPassword", 1, RegistryValueKind.DWord);
            }
        }

        public string GetProcessName(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    return process.HasExited ? null : process.ProcessName;
                }
            }
            catch (ArgumentException)
            {
                return null; // no such process
            }
            catch (InvalidOperationException)
            {
                return null; // exited while inspecting
            }
        }

        public int StartProcess(string fileName, string arguments)
        {
            var info = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(fileName) ?? string.Empty,
            };
            using (var process = Process.Start(info))
            {
                return process?.Id ?? 0;
            }
        }

        public bool FileExists(string path) => File.Exists(path);

        public DateTime GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);

        public string ReadAllText(string path) => File.ReadAllText(path, Encoding.UTF8);

        public void WriteAllTextAtomic(string path, string contents)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = path + ".tmp";
            File.WriteAllText(temp, contents, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }

        private static object ReadUserValue(string subKey, string name)
        {
            var keyPath = string.IsNullOrEmpty(subKey) ? UserSteamKey : UserSteamKey + "\\" + subKey;
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath))
            {
                return key?.GetValue(name);
            }
        }
    }
}
