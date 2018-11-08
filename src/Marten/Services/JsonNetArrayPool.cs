using System.Buffers;
using Newtonsoft.Json;

namespace Marten.Services
{
    public class JsonNetArrayPool : IArrayPool<char>
    {
        public static readonly JsonNetArrayPool Shared = new JsonNetArrayPool(ArrayPool<char>.Shared);

        private readonly ArrayPool<char> _arrayPool;

        public JsonNetArrayPool(ArrayPool<char> arrayPool)
        {
            _arrayPool = arrayPool;
        }

        public char[] Rent(int minimumLength)
        {
            return _arrayPool.Rent(minimumLength);
        }

        public void Return(char[] array)
        {
            _arrayPool.Return(array);
        }
    }
}