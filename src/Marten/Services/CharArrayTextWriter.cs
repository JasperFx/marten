using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Marten.Services
{
    public sealed class CharArrayTextWriter : TextWriter
    {
        public const int InitialSize = 4096;
        static readonly Encoding EncodingValue = new UnicodeEncoding(false, false);
        char[] _chars = new char[InitialSize];
        int _next;
        int _length = InitialSize;

        public override Encoding Encoding => EncodingValue;
        public static readonly IPool DefaultPool = new Pool();

        public override void Write(char value)
        {
            Ensure(1);
            _chars[_next] = value;
            _next += 1;
        }

        void Ensure(int i)
        {
            var required = _next + i;
            if (required < _length)
            {
                return;
            }

            while (required >= _length)
            {
                _length *= 2;
            }
            Array.Resize(ref _chars, _length);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            Ensure(count);
            Array.Copy(buffer, index, _chars, _next, count);
            _next += count;
        }

        public override void Write(string value)
        {
            var length = value.Length;
            Ensure(length);
            value.CopyTo(0, _chars, _next, length);
            _next += length;
        }

        public override Task WriteAsync(char value)
        {
            Write(value);
            return Task.CompletedTask;
        }

        public override Task WriteAsync(string value)
        {
            Write(value);
            return Task.CompletedTask;
        }

        public override Task WriteAsync(char[] buffer, int index, int count)
        {
            Write(buffer, index, count);
            return Task.CompletedTask;
        }

        public override Task WriteLineAsync(char value)
        {
            WriteLine(value);
            return Task.CompletedTask;
        }

        public override Task WriteLineAsync(string value)
        {
            WriteLine(value);
            return Task.CompletedTask;
        }

        public override Task WriteLineAsync(char[] buffer, int index, int count)
        {
            WriteLine(buffer, index, count);
            return Task.CompletedTask;
        }

        public override Task FlushAsync()
        {
            return Task.CompletedTask;
        }

        public char[] Buffer => _chars;
        public int Size => _next;

        public interface IPool
        {
            CharArrayTextWriter Lease();
            void Release(CharArrayTextWriter writer);
            void Release(IEnumerable<CharArrayTextWriter> writer);
        }

        public class Pool : IPool, IDisposable
        {
            readonly IPool _parent;
            readonly ConcurrentStack<CharArrayTextWriter> _cache = new ConcurrentStack<CharArrayTextWriter>();

            public Pool(IPool parent)
            {
                _parent = parent;
            }

            public Pool() : this(null)
            {}
            
            public CharArrayTextWriter Lease()
            {
                CharArrayTextWriter writer;
                if (_cache.TryPop(out writer))
                {
                    return writer;
                }

                writer = _parent?.Lease();
                if (writer != null)
                {
                    return writer;
                }
                
                return new CharArrayTextWriter();
            }

            public void Release(CharArrayTextWriter writer)
            {
                // currently, all writers are cached. This might be changed to hold only N writers in the cache.
                writer.Clear();
                _cache.Push(writer);
            }

            public void Release(IEnumerable<CharArrayTextWriter> writer)
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
                    writers[i]._next = 0;
                }
                _cache.PushRange(writers);
            }

            public void Dispose()
            {
                if (_parent != null)
                {
                    _parent.Release(_cache);
                    _cache.Clear();
                }
            }
        }

        public ArraySegment<char> ToCharSegment()
        {
            return new ArraySegment<char>(Buffer, 0, Size);
        }

        public void Clear()
        {
            _next = 0;
        }
    }
}