using SteamAccountSwitcher.Core.Decision;
using Xunit;

namespace SteamAccountSwitcher.Core.Tests
{
    public class AccountPagingTests
    {
        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(3, 1)]
        [InlineData(4, 2)]
        [InlineData(5, 3)]
        [InlineData(6, 3)]
        [InlineData(7, 4)]
        public void Page_count(int total, int expected)
        {
            Assert.Equal(expected, AccountPaging.PageCount(total));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(7)]
        public void A_page_never_has_more_buttons_than_fit(int total)
        {
            for (var page = 0; page < AccountPaging.PageCount(total); page++)
            {
                var buttons = AccountPaging.Count(total, page) + (AccountPaging.HasMore(total) ? 1 : 0) + 1;
                Assert.True(buttons <= AccountPaging.MaxButtons, $"total={total} page={page} buttons={buttons}");
            }
        }

        [Fact]
        public void Up_to_three_accounts_fit_on_one_page_without_more()
        {
            Assert.Equal(0, AccountPaging.Start(3, 0));
            Assert.Equal(3, AccountPaging.Count(3, 0));
            Assert.False(AccountPaging.HasMore(3));
        }

        [Fact]
        public void Four_accounts_split_two_and_two()
        {
            Assert.True(AccountPaging.HasMore(4));
            Assert.Equal(0, AccountPaging.Start(4, 0));
            Assert.Equal(2, AccountPaging.Count(4, 0));
            Assert.Equal(2, AccountPaging.Start(4, 1));
            Assert.Equal(2, AccountPaging.Count(4, 1));
        }

        [Fact]
        public void More_on_last_page_wraps_to_first()
        {
            Assert.Equal(0, AccountPaging.Start(5, 3));
            Assert.Equal(2, AccountPaging.Count(5, 3));
            Assert.Equal(4, AccountPaging.Start(5, 2));
            Assert.Equal(1, AccountPaging.Count(5, 2));
        }

        [Fact]
        public void Picker_opens_on_page_of_preferred_account()
        {
            Assert.Equal(0, AccountPaging.PageOf(1, 4));
            Assert.Equal(1, AccountPaging.PageOf(2, 4));
            Assert.Equal(1, AccountPaging.PageOf(3, 4));
            Assert.Equal(0, AccountPaging.PageOf(2, 3));
            Assert.Equal(0, AccountPaging.PageOf(-1, 4));
        }

        [Fact]
        public void No_accounts()
        {
            Assert.Equal(0, AccountPaging.Count(0, 0));
            Assert.False(AccountPaging.HasMore(0));
        }
    }
}
