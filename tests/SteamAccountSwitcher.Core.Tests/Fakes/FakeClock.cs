using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SteamAccountSwitcher.Core;

namespace SteamAccountSwitcher.Core.Tests.Fakes
{
    /// <summary>Virtual time: Sleep advances the clock instantly and runs actions scheduled with <see cref="At"/>.</summary>
    public sealed class FakeClock : IClock
    {
        private readonly List<KeyValuePair<DateTime, Action>> scheduled = new List<KeyValuePair<DateTime, Action>>();

        public DateTime UtcNow { get; private set; } = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        public TimeSpan Elapsed => UtcNow - Start;

        public DateTime Start { get; } = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>Called on every Sleep after time advanced, e.g. to cancel a token mid-wait.</summary>
        public Action OnSleep { get; set; }

        /// <summary>Runs <paramref name="action"/> once virtual time reaches now + <paramref name="delay"/>.</summary>
        public void At(TimeSpan delay, Action action) => scheduled.Add(new KeyValuePair<DateTime, Action>(UtcNow + delay, action));

        public void Sleep(TimeSpan duration, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UtcNow += duration;
            RunDue();
            OnSleep?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
        }

        private void RunDue()
        {
            while (true)
            {
                var due = scheduled.Where(s => s.Key <= UtcNow).OrderBy(s => s.Key).FirstOrDefault();
                if (due.Value == null)
                {
                    return;
                }

                scheduled.Remove(due);
                due.Value();
            }
        }
    }
}
