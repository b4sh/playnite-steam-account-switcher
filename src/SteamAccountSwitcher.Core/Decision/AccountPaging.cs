using System;

namespace SteamAccountSwitcher.Core.Decision
{
    /// <summary>
    /// Splits the account picker into pages (SPEC §7.5): Playnite's message box lays its options out in one row inside
    /// a fixed-size box, so a page holds at most <see cref="Slots"/> account / "More accounts…" buttons. Pure.
    /// </summary>
    public sealed class AccountPaging
    {
        /// <summary>
        /// Buttons per message box. Fullscreen lays them out in one horizontal row (min. 150 px each in the default
        /// theme); 5 buttons didn't fit in testing (2026-10-24).
        /// </summary>
        public const int MaxButtons = 4;

        /// <param name="reservedButtons">Buttons that aren't accounts or "More" (the Cancel button in Desktop mode).</param>
        public AccountPaging(int total, int reservedButtons)
        {
            Total = Math.Max(total, 0);
            Slots = Math.Max(MaxButtons - reservedButtons, 2);
        }

        public int Total { get; }

        /// <summary>Buttons available for accounts and "More accounts…".</summary>
        public int Slots { get; }

        /// <summary>Accounts per page when paging: one slot is taken by "More accounts…".</summary>
        public int PageSize => Slots - 1;

        /// <summary>Up to <see cref="Slots"/> accounts fit on one page: the "More" slot isn't needed then.</summary>
        public int PageCount => Total <= Slots ? 1 : (Total + PageSize - 1) / PageSize;

        /// <summary>The page shows a "More accounts…" option.</summary>
        public bool HasMore => PageCount > 1;

        /// <summary>First account index of <paramref name="page"/> (wrapped, so "More" on the last page returns to the first).</summary>
        public int Start(int page) => PageCount == 1 ? 0 : Wrap(page) * PageSize;

        public int Count(int page) => PageCount == 1 ? Total : Math.Min(PageSize, Total - Start(page));

        /// <summary>Page containing account <paramref name="index"/>; 0 for -1 (no preferred account).</summary>
        public int PageOf(int index) => index < 0 || PageCount == 1 ? 0 : index / PageSize;

        private int Wrap(int page) => ((page % PageCount) + PageCount) % PageCount;
    }
}
