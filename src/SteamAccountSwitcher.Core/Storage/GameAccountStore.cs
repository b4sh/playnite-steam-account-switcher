using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using SteamAccountSwitcher.Core.Steam;

namespace SteamAccountSwitcher.Core.Storage
{
    /// <summary>Game → Steam account assignments, persisted as accounts.json (SPEC §8). Thread-safe.</summary>
    public sealed class GameAccountStore
    {
        public const int CurrentVersion = 1;

        private readonly ISteamEnvironment environment;
        private readonly string path;
        private readonly object sync = new object();
        private Dictionary<Guid, string> games = new Dictionary<Guid, string>();

        public GameAccountStore(ISteamEnvironment environment, string path)
        {
            this.environment = environment ?? throw new ArgumentNullException(nameof(environment));
            this.path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public int Count
        {
            get
            {
                lock (sync)
                {
                    return games.Count;
                }
            }
        }

        /// <summary>Loads the file; a missing file means no assignments.</summary>
        /// <exception cref="InvalidDataException">The file exists but is not valid.</exception>
        public void Load()
        {
            var loaded = new Dictionary<Guid, string>();
            if (environment.FileExists(path))
            {
                var dto = Deserialize(environment.ReadAllText(path));
                foreach (var pair in dto.Games ?? new Dictionary<string, string>())
                {
                    if (Guid.TryParse(pair.Key, out var id) && !string.IsNullOrEmpty(pair.Value))
                    {
                        loaded[id] = pair.Value;
                    }
                }
            }

            lock (sync)
            {
                games = loaded;
            }
        }

        public void Save()
        {
            StoreDto dto;
            lock (sync)
            {
                dto = new StoreDto
                {
                    Version = CurrentVersion,
                    Games = games.OrderBy(p => p.Key).ToDictionary(p => p.Key.ToString(), p => p.Value),
                };
            }

            environment.WriteAllTextAtomic(path, Serialize(dto));
        }

        /// <summary>Assigned account name, or null.</summary>
        public string Get(Guid gameId)
        {
            lock (sync)
            {
                return games.TryGetValue(gameId, out var name) ? name : null;
            }
        }

        /// <summary>Assigns <paramref name="accountName"/> to all games; null/empty removes the assignments.</summary>
        public void Assign(IEnumerable<Guid> gameIds, string accountName)
        {
            lock (sync)
            {
                foreach (var id in gameIds)
                {
                    if (string.IsNullOrEmpty(accountName))
                    {
                        games.Remove(id);
                    }
                    else
                    {
                        games[id] = accountName;
                    }
                }
            }
        }

        /// <summary>Drops assignments of games that are no longer in the library; returns how many were removed.</summary>
        public int RemoveMissing(ICollection<Guid> existingGameIds)
        {
            lock (sync)
            {
                var missing = games.Keys.Where(id => !existingGameIds.Contains(id)).ToList();
                foreach (var id in missing)
                {
                    games.Remove(id);
                }

                return missing.Count;
            }
        }

        private static DataContractJsonSerializer CreateSerializer() =>
            new DataContractJsonSerializer(typeof(StoreDto), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        internal static string Serialize(StoreDto dto)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, new UTF8Encoding(false), ownsStream: false, indent: true))
                {
                    CreateSerializer().WriteObject(writer, dto);
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        internal static StoreDto Deserialize(string json)
        {
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var dto = (StoreDto)CreateSerializer().ReadObject(stream);
                    if (dto == null)
                    {
                        throw new InvalidDataException("accounts.json is empty.");
                    }

                    if (dto.Version > CurrentVersion)
                    {
                        throw new InvalidDataException($"accounts.json version {dto.Version} is newer than supported ({CurrentVersion}).");
                    }

                    return dto;
                }
            }
            catch (SerializationException ex)
            {
                throw new InvalidDataException("accounts.json is not valid JSON.", ex);
            }
        }

        [DataContract]
        internal sealed class StoreDto
        {
            [DataMember(Name = "version", Order = 0)]
            public int Version { get; set; }

            [DataMember(Name = "games", Order = 1)]
            public Dictionary<string, string> Games { get; set; }
        }
    }
}
