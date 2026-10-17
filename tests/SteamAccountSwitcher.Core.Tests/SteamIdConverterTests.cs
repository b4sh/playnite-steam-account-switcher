using System;
using SteamAccountSwitcher.Core.Steam;
using Xunit;

namespace SteamAccountSwitcher.Core.Tests
{
    public class SteamIdConverterTests
    {
        [Theory]
        [InlineData(76561197960265728UL, 0u)]
        [InlineData(76561197960265729UL, 1u)]
        [InlineData(76561197960287930UL, 22202u)]
        [InlineData(76561202255233023UL, uint.MaxValue)]
        public void Converts_both_ways(ulong steamId64, uint accountId)
        {
            Assert.Equal(accountId, SteamIdConverter.ToAccountId(steamId64));
            Assert.Equal(steamId64, SteamIdConverter.ToSteamId64(accountId));
        }

        [Theory]
        [InlineData(0UL)]
        [InlineData(76561197960265727UL)]
        [InlineData(76561202255233024UL)]
        public void ToAccountId_rejects_ids_outside_individual_range(ulong steamId64)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SteamIdConverter.ToAccountId(steamId64));
        }

        [Theory]
        [InlineData("76561197960287930", true, 22202u)]
        [InlineData("not a number", false, 0u)]
        [InlineData("", false, 0u)]
        [InlineData("12345", false, 0u)]
        public void TryParseAccountId(string input, bool ok, uint expected)
        {
            Assert.Equal(ok, SteamIdConverter.TryParseAccountId(input, out var accountId));
            Assert.Equal(expected, accountId);
        }
    }
}
