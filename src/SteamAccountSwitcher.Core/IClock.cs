using System;
using System.Threading;

namespace SteamAccountSwitcher.Core
{
    /// <summary>Time source for polling loops, so tests can run timeouts without real waiting.</summary>
    public interface IClock
    {
        DateTime UtcNow { get; }

        /// <summary>Waits for <paramref name="duration"/>; throws <see cref="OperationCanceledException"/> when cancelled.</summary>
        void Sleep(TimeSpan duration, CancellationToken cancellationToken);
    }

    public sealed class SystemClock : IClock
    {
        public static readonly SystemClock Instance = new SystemClock();

        public DateTime UtcNow => DateTime.UtcNow;

        public void Sleep(TimeSpan duration, CancellationToken cancellationToken)
        {
            if (cancellationToken.WaitHandle.WaitOne(duration))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}
