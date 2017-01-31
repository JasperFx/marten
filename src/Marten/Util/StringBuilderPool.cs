using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Marten.Util
{
    public class StringBuilderPool
    {

        private readonly StringBuilderPool _parent;
        private readonly ConcurrentStack<StringBuilder> _cache = new ConcurrentStack<StringBuilder>();

        public StringBuilderPool(StringBuilderPool parent)
        {
            _parent = parent;
        }

        public StringBuilder Lease()
        {
            StringBuilder writer;
            if (_cache.TryPop(out writer))
            {
                return writer;
            }

            writer = _parent?.Lease();
            if (writer != null)
            {
                return writer;
            }

            return new StringBuilder();
        }

        public void Release(StringBuilder writer)
        {
            // currently, all writers are cached. This might be changed to hold only N writers in the cache.
            writer.Clear();
            _cache.Push(writer);
        }

        public void Release(IEnumerable<StringBuilder> writer)
        {
            // currently, all writers are cached. This might be changed to hold only N writers in the cache.
            var writers = writer.ToArray();
            if (writers.Length == 0)
            {
                return;
            }

            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < writers.Length; i++)
            {
                writers[i].Clear();
            }

            _cache.PushRange(writers);
        }

        public void Dispose()
        {
            _parent?.Release(_cache);
            _cache.Clear();
        }
    }
}