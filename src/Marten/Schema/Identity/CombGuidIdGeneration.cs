using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
#nullable enable
namespace Marten.Schema.Identity
{
    /// <summary>
    ///     Comb Guid Id Generation. More info http://www.informit.com/articles/article.aspx?p=25862
    /// </summary>
    public class CombGuidIdGeneration: IIdGeneration
    {
        private const int NumDateBytes = 6;

        public IEnumerable<Type> KeyTypes { get; } = new[] { typeof(Guid) };

        public bool RequiresSequences { get; } = false;
        public void GenerateCode(GeneratedMethod method, DocumentMapping mapping)
        {
            var document = new Use(mapping.DocumentType);
            method.Frames.Code($"if ({{0}}.{mapping.IdMember.Name} == Guid.Empty) _setter({{0}}, {typeof(CombGuidIdGeneration).FullNameInCode()}.NewGuid());", document);
            method.Frames.Code($"return {{0}}.{mapping.IdMember.Name};", document);
        }

        /*
            FROM: https://github.com/richardtallent/RT.Comb/blob/master/RT.Comb/RT.CombByteOrder.Comb.cs

            Copyright 2015 Richard S. Tallent, II
            Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files
            (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge,
            publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to
            do so, subject to the following conditions:
            The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.
            THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
            MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
            LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
            CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
        */

        /// <summary>
        ///     Returns a new Guid COMB, consisting of a random Guid combined with the provided timestamp.
        /// </summary>
        public static Guid NewGuid(DateTimeOffset timestamp) => Create(Guid.NewGuid(), timestamp);

        public static Guid NewGuid() => Create(Guid.NewGuid(), DateTimeOffset.UtcNow);

        private static void WriteDateTime(Span<byte> destination, DateTimeOffset timestamp)
        {
            var unixTime = timestamp.ToUnixTimeMilliseconds();
            Span<byte> unixTimeBytes = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(unixTimeBytes, unixTime);

            unixTimeBytes.Slice(2, 4).CopyTo(destination);
            unixTimeBytes.Slice(0, 2).CopyTo(destination.Slice(4));
        }

        private static DateTimeOffset BytesToDateTime(ReadOnlySpan<byte> value)
        {
            Span<byte> unixTimeBytes = stackalloc byte[8];
            value.Slice(4, 2).CopyTo(unixTimeBytes);
            value.Slice(0, 4).CopyTo(unixTimeBytes.Slice(2));
            unixTimeBytes.Slice(6).Clear();
            var unixTime = BinaryPrimitives.ReadInt64LittleEndian(unixTimeBytes);

            return DateTimeOffset.FromUnixTimeMilliseconds(unixTime);
        }

        public static Guid Create(Guid value, DateTimeOffset timestamp)
        {
#if NET5_0_OR_GREATER
            Span<byte> bytes = stackalloc byte[16];
            value.TryWriteBytes(bytes);
#else
            var bytes = value.ToByteArray();
#endif

            // Overwrite the first six bytes with unix time
            WriteDateTime(bytes, timestamp);
            return new Guid(bytes);
        }

        public static DateTimeOffset GetTimestamp(Guid comb)
        {
#if NET5_0_OR_GREATER
            Span<byte> bytes = stackalloc byte[16];
            comb.TryWriteBytes(bytes);
#else
            var bytes = comb.ToByteArray();
#endif
            return BytesToDateTime(bytes);
        }
    }
}
