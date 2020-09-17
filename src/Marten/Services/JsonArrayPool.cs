using System;
using System.Buffers;
using Newtonsoft.Json;

namespace Marten.Services
{
    internal class JsonArrayPool<T> : IArrayPool<T>
    {
        private readonly ArrayPool<T> _inner;

        public JsonArrayPool(ArrayPool<T> inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public T[] Rent(int minimumLength)
        {
            return _inner.Rent(minimumLength);
        }

        public void Return(T[] array)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));

            _inner.Return(array);
        }
    }
}
