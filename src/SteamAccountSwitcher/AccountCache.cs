using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Playnite.SDK;
using SteamAccountSwitcher.Core.Steam;

namespace SteamAccountSwitcher
{
    /// <summary>
    /// In-memory list of Steam accounts so GetGameMenuItems never does I/O (SPEC §7.1).
    /// Reloaded on start, on demand and whenever loginusers.vdf changes (FileSystemWatcher).
    /// </summary>
    public sealed class AccountCache : IDisposable
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(500);

        private readonly ISteamEnvironment environment;
        private readonly object sync = new object();
        private IReadOnlyList<SteamAccount> accounts = new SteamAccount[0];
        private SteamInstallation installation;
        private FileSystemWatcher watcher;
        private Timer debounceTimer;

        public AccountCache(ISteamEnvironment environment)
        {
            this.environment = environment;
        }

        public event EventHandler Changed;

        public IReadOnlyList<SteamAccount> Accounts => accounts;

        /// <summary>Points the cache at a Steam installation (null = not found), starts watching and reloads.</summary>
        public void Initialize(SteamInstallation steam)
        {
            lock (sync)
            {
                installation = steam;
                StopWatching();
                var configDir = steam == null ? null : Path.GetDirectoryName(steam.LoginUsersPath);
                if (configDir != null && Directory.Exists(configDir))
                {
                    watcher = new FileSystemWatcher(configDir, Path.GetFileName(steam.LoginUsersPath))
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    };
                    watcher.Changed += OnFileEvent;
                    watcher.Created += OnFileEvent;
                    watcher.Renamed += OnFileEvent;
                    watcher.EnableRaisingEvents = true;
                    debounceTimer = new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
                }
            }

            Reload();
        }

        /// <summary>Re-reads loginusers.vdf; on a read/parse error keeps the previous list.</summary>
        public void Reload()
        {
            var steam = installation;
            IReadOnlyList<SteamAccount> loaded;
            try
            {
                loaded = steam == null ? new SteamAccount[0] : LoginUsersFile.Load(environment, steam.LoginUsersPath);
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is UnauthorizedAccessException)
            {
                // Steam may be writing the file right now; the watcher fires again when it is done.
                Logger.Warn(ex, "Could not read loginusers.vdf, keeping previous account list.");
                return;
            }

            accounts = loaded;
            Logger.Debug($"Loaded {loaded.Count} Steam account(s).");
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            lock (sync)
            {
                StopWatching();
            }
        }

        private void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            debounceTimer?.Change(Debounce, Timeout.InfiniteTimeSpan);
        }

        private void StopWatching()
        {
            watcher?.Dispose();
            watcher = null;
            debounceTimer?.Dispose();
            debounceTimer = null;
        }
    }
}
