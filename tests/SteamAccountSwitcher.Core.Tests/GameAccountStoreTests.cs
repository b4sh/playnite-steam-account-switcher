using System;
using System.IO;
using SteamAccountSwitcher.Core.Storage;
using SteamAccountSwitcher.Core.Tests.Fakes;
using Xunit;

namespace SteamAccountSwitcher.Core.Tests
{
    public class GameAccountStoreTests
    {
        private const string FilePath = @"C:\Playnite\ExtensionsData\sas\accounts.json";

        private static readonly Guid Game1 = new Guid("11111111-1111-1111-1111-111111111111");
        private static readonly Guid Game2 = new Guid("22222222-2222-2222-2222-222222222222");
        private static readonly Guid Game3 = new Guid("33333333-3333-3333-3333-333333333333");

        private readonly FakeSteamEnvironment env = new FakeSteamEnvironment();

        [Fact]
        public void Missing_file_means_no_assignments()
        {
            var store = new GameAccountStore(env, FilePath);

            store.Load();

            Assert.Equal(0, store.Count);
            Assert.Null(store.Get(Game1));
        }

        [Fact]
        public void Save_and_load_round_trip()
        {
            var store = new GameAccountStore(env, FilePath);
            store.Assign(new[] { Game1, Game2 }, "alpha");
            store.Assign(new[] { Game3 }, "beta");
            store.Save();

            var reloaded = new GameAccountStore(env, FilePath);
            reloaded.Load();

            Assert.Equal(3, reloaded.Count);
            Assert.Equal("alpha", reloaded.Get(Game1));
            Assert.Equal("alpha", reloaded.Get(Game2));
            Assert.Equal("beta", reloaded.Get(Game3));
            Assert.Contains("write: " + FilePath, env.Log);
        }

        [Fact]
        public void Saved_json_matches_documented_format()
        {
            var store = new GameAccountStore(env, FilePath);
            store.Assign(new[] { Game1 }, "alpha");
            store.Save();

            var json = env.Files[FilePath];
            Assert.Contains("\"version\":1", json.Replace(" ", string.Empty));
            Assert.Contains("\"11111111-1111-1111-1111-111111111111\":\"alpha\"", json.Replace(" ", string.Empty));
        }

        [Fact]
        public void Reads_hand_written_json()
        {
            env.Files[FilePath] = "{ \"version\": 1, \"games\": { \"" + Game2 + "\": \"beta\", \"not-a-guid\": \"x\", \"" + Game3 + "\": \"\" } }";
            var store = new GameAccountStore(env, FilePath);

            store.Load();

            Assert.Equal(1, store.Count);
            Assert.Equal("beta", store.Get(Game2));
        }

        [Fact]
        public void Assigning_null_or_empty_removes_assignment()
        {
            var store = new GameAccountStore(env, FilePath);
            store.Assign(new[] { Game1, Game2, Game3 }, "alpha");

            store.Assign(new[] { Game1 }, null);
            store.Assign(new[] { Game2 }, string.Empty);

            Assert.Null(store.Get(Game1));
            Assert.Null(store.Get(Game2));
            Assert.Equal("alpha", store.Get(Game3));
        }

        [Fact]
        public void RemoveMissing_drops_games_no_longer_in_library()
        {
            var store = new GameAccountStore(env, FilePath);
            store.Assign(new[] { Game1, Game2, Game3 }, "alpha");

            var removed = store.RemoveMissing(new[] { Game2 });

            Assert.Equal(2, removed);
            Assert.Equal(1, store.Count);
            Assert.Equal("alpha", store.Get(Game2));
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("{ \"version\": 99, \"games\": { } }")]
        public void Invalid_file_throws_InvalidDataException(string content)
        {
            env.Files[FilePath] = content;

            Assert.Throws<InvalidDataException>(() => new GameAccountStore(env, FilePath).Load());
        }
    }
}
