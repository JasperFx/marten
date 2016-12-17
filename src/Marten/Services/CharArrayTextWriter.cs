using System;
using System.IO;
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

        public override void Write(char value)
        {
            Ensure(1);
            _chars[_next] = value;
            _next += 1;
        }

        void Ensure(int i)
        {
            if (_next + i >= _length)
            {
                _length *= 2;
                Array.Resize(ref _chars, _length);
            }
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

        public ArraySegment<char> ToRawArraySegment()
        {
            return new ArraySegment<char>(_chars,0, _next);
        }
    }
}