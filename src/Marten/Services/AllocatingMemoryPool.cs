using System;
using System.Buffers;

namespace Marten.Services
{
    class AllocatingMemoryPool<T> : MemoryPool<T>
    {
        public override IMemoryOwner<T> Rent(int minBufferSize = -1) => new MemoryOwner(minBufferSize);

        public static readonly AllocatingMemoryPool<T> Instance = new AllocatingMemoryPool<T>();

        internal class MemoryOwner : IMemoryOwner<T>
        {

            public MemoryOwner(int minBufferSize)
            {
                Memory = new Memory<T>(new T[minBufferSize < 0 ? 1024 : minBufferSize]);
            }

            public void Dispose()
            {
            }

            public Memory<T> Memory { get; }
        }

        public override int MaxBufferSize => int.MaxValue;
        protected override void Dispose(bool disposing)
        { }
    }
}