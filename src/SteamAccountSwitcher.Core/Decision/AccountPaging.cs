using System;

namespace SteamAccountSwitcher.Core.Decision
{
    /// <summary>
    /// Splits the account picker into pages (SPEC §7.5): Playnite's message box lays its options out in one row,
    /// so each page shows a few accounts plus "More accounts…" and Cancel. Pure.
    /// </summary>
    public static class AccountPaging
    {
        /// <summary>
        /// Buttons per message box, Cancel included. Fullscreen lays them out in one horizontal row inside a fixed-size box
        /// (min. 150 px each in the default theme); 5 buttons didn't fit in testing (2026-10-24).
        /// </summary>
        public const int MaxButtons = 4;

        /// <summary>Accounts per page when paging: room is left for "More accounts…" and Cancel.</summary>
        public const int PageSize = MaxButtons - 2;

        /// <summary>Up to PageSize + 1 accounts fit on one page: the "More" slot isn't needed then.</summary>
        public static int PageCount(int total) =>
            total <= PageSize + 1 ? 1 : (total + PageSize - 1) / PageSize;

        /// <summary>First account index of <paramref name="page"/> (wrapped, so "More" on the last page returns to the first).</summary>
        public static int Start(int total, int page) => PageCount(total) == 1 ? 0 : Wrap(page, total) * PageSize;

        public static int Count(int total, int page) =>
            PageCount(total) == 1 ? Math.Max(total, 0) : Math.Min(PageSize, total - Start(total, page));

        /// <summary>The page shows a "More accounts…" option.</summary>
        public static bool HasMore(int total) => PageCount(total) > 1;

        /// <summary>Page containing account <paramref name="index"/>; 0 for -1 (no preferred account).</summary>
        public static int PageOf(int index, int total) =>
            index < 0 || PageCount(total) == 1 ? 0 : index / PageSize;

        private static int Wrap(int page, int total)
        {
            var count = PageCount(total);
            return ((page % count) + count) % count;
        }
    }
}
