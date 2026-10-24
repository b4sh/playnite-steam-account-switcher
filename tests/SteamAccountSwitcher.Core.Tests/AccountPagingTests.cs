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
        [InlineData(4, 1)]
        [InlineData(5, 2)]
        [InlineData(6, 2)]
        [InlineData(7, 3)]
        public void Page_count(int total, int expected)
        {
            Assert.Equal(expected, AccountPaging.PageCount(total));
        }

        [Fact]
        public void Up_to_four_accounts_fit_on_one_page_without_more()
        {
            Assert.Equal(0, AccountPaging.Start(4, 0));
            Assert.Equal(4, AccountPaging.Count(4, 0));
            Assert.False(AccountPaging.HasMore(4));
        }

        [Fact]
        public void Five_accounts_split_three_and_two()
        {
            Assert.True(AccountPaging.HasMore(5));
            Assert.Equal(0, AccountPaging.Start(5, 0));
            Assert.Equal(3, AccountPaging.Count(5, 0));
            Assert.Equal(3, AccountPaging.Start(5, 1));
            Assert.Equal(2, AccountPaging.Count(5, 1));
        }

        [Fact]
        public void More_on_last_page_wraps_to_first()
        {
            Assert.Equal(0, AccountPaging.Start(7, 3));
            Assert.Equal(3, AccountPaging.Count(7, 3));
            Assert.Equal(6, AccountPaging.Start(7, 2));
            Assert.Equal(1, AccountPaging.Count(7, 2));
        }

        [Fact]
        public void Picker_opens_on_page_of_preferred_account()
        {
            Assert.Equal(0, AccountPaging.PageOf(2, 7));
            Assert.Equal(1, AccountPaging.PageOf(3, 7));
            Assert.Equal(2, AccountPaging.PageOf(6, 7));
            Assert.Equal(0, AccountPaging.PageOf(3, 4));
            Assert.Equal(0, AccountPaging.PageOf(-1, 7));
        }

        [Fact]
        public void No_accounts()
        {
            Assert.Equal(0, AccountPaging.Count(0, 0));
            Assert.False(AccountPaging.HasMore(0));
        }
    }
}
