using SteamAccountSwitcher.Core.Decision;
using Xunit;

namespace SteamAccountSwitcher.Core.Tests
{
    public class AccountPagingTests
    {
        // Fullscreen: no Cancel button (the controller's cancel button closes the picker).
        private static AccountPaging Fullscreen(int total) => new AccountPaging(total, 0);

        // Desktop: a Cancel button takes one slot.
        private static AccountPaging Desktop(int total) => new AccountPaging(total, 1);

        [Theory]
        [InlineData(0, 1)]
        [InlineData(4, 1)]
        [InlineData(5, 2)]
        [InlineData(6, 2)]
        [InlineData(7, 3)]
        public void Fullscreen_page_count(int total, int expected)
        {
            Assert.Equal(expected, Fullscreen(total).PageCount);
        }

        [Theory]
        [InlineData(3, 1)]
        [InlineData(4, 2)]
        [InlineData(5, 3)]
        public void Desktop_page_count(int total, int expected)
        {
            Assert.Equal(expected, Desktop(total).PageCount);
        }

        [Theory]
        [InlineData(1, 0)]
        [InlineData(4, 0)]
        [InlineData(7, 0)]
        [InlineData(3, 1)]
        [InlineData(4, 1)]
        [InlineData(7, 1)]
        public void A_page_never_has_more_buttons_than_fit(int total, int reserved)
        {
            var paging = new AccountPaging(total, reserved);
            for (var page = 0; page < paging.PageCount; page++)
            {
                var buttons = paging.Count(page) + (paging.HasMore ? 1 : 0) + reserved;
                Assert.True(buttons <= AccountPaging.MaxButtons, $"total={total} reserved={reserved} page={page} buttons={buttons}");
            }
        }

        [Fact]
        public void Four_accounts_fit_on_one_fullscreen_page()
        {
            var paging = Fullscreen(4);

            Assert.False(paging.HasMore);
            Assert.Equal(0, paging.Start(0));
            Assert.Equal(4, paging.Count(0));
        }

        [Fact]
        public void Five_accounts_split_three_and_two_in_fullscreen()
        {
            var paging = Fullscreen(5);

            Assert.True(paging.HasMore);
            Assert.Equal(3, paging.Count(0));
            Assert.Equal(3, paging.Start(1));
            Assert.Equal(2, paging.Count(1));
        }

        [Fact]
        public void More_on_last_page_wraps_to_first()
        {
            var paging = Desktop(5);

            Assert.Equal(0, paging.Start(3));
            Assert.Equal(2, paging.Count(3));
            Assert.Equal(4, paging.Start(2));
            Assert.Equal(1, paging.Count(2));
        }

        [Fact]
        public void Picker_opens_on_page_of_preferred_account()
        {
            Assert.Equal(0, Desktop(4).PageOf(1));
            Assert.Equal(1, Desktop(4).PageOf(2));
            Assert.Equal(0, Fullscreen(4).PageOf(3));
            Assert.Equal(1, Fullscreen(7).PageOf(3));
            Assert.Equal(0, Fullscreen(7).PageOf(-1));
        }

        [Fact]
        public void No_accounts()
        {
            Assert.Equal(0, Fullscreen(0).Count(0));
            Assert.False(Fullscreen(0).HasMore);
        }
    }
}
