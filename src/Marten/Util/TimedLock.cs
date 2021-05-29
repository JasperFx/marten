using System;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Util
{
    internal class TimedLock
    {
        private readonly SemaphoreSlim _toLock;

        public TimedLock()
        {
            _toLock = new SemaphoreSlim(1, 1);
        }

        public async Task<LockReleaser> Lock(TimeSpan timeout)
        {
            if(await _toLock.WaitAsync(timeout))
            {
                return new LockReleaser(_toLock);
            }
            throw new TimeoutException();
        }

        public struct LockReleaser : IDisposable
        {
            private readonly SemaphoreSlim toRelease;

            public LockReleaser(SemaphoreSlim toRelease)
            {
                this.toRelease = toRelease;
            }
            public void Dispose()
            {
                toRelease.Release();
            }
        }
    }
}
