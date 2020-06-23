using System;
using System.Collections.Generic;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;

namespace Marten.Schema.Identity
{
    /// <summary>
    ///     Comb Guid Id Generation. More info http://www.informit.com/articles/article.aspx?p=25862
    /// </summary>
    public class CombGuidIdGeneration: IIdGeneration
    {
        private const int NumDateBytes = 6;

        public IEnumerable<Type> KeyTypes { get; } = new[] { typeof(Guid) };

        public IIdGenerator<T> Build<T>()
        {
            return (IIdGenerator<T>)new GuidIdGenerator(() => Create(Guid.NewGuid(), DateTime.UtcNow));
        }

        public bool RequiresSequences { get; } = false;
        public void GenerateCode(GeneratedMethod method, DocumentMapping mapping)
        {
            var document = new Use(mapping.DocumentType);
            method.Frames.Code($"if ({{0}}.{mapping.IdMember.Name} == Guid.Empty) {{0}}.Id = {typeof(CombGuidIdGeneration).FullNameInCode()}.NewGuid();", document);
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

        private static byte[] DateTimeToBytes(DateTimeOffset timestamp)
        {
            var unixTime = timestamp.ToUnixTimeMilliseconds();
            var unixTimeBytes = BitConverter.GetBytes(unixTime);

            var result = new byte[NumDateBytes];

            if (BitConverter.IsLittleEndian)
            {
                Array.Copy(unixTimeBytes, 2, result, 0, 4);
                Array.Copy(unixTimeBytes, 0, result, 4, 2);
            }
            else
            {
                Array.Copy(unixTimeBytes, 2, result, 0, 6);
            }

            return result;
        }

        private static DateTimeOffset BytesToDateTime(byte[] value)
        {
            var unixTimeBytes = new byte[8];

            if (BitConverter.IsLittleEndian)
            {
                Array.Copy(value, 4, unixTimeBytes, 0, 2);
                Array.Copy(value, 0, unixTimeBytes, 2, 4);
            }
            else
            {
                Array.Copy(value, 0, unixTimeBytes, 2, 6);
            }

            var unixTime = BitConverter.ToInt64(unixTimeBytes, 0);
            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(0).AddMilliseconds(unixTime);
            return timestamp;
        }

        public static Guid Create(Guid value, DateTimeOffset timestamp)
        {
            var bytes = value.ToByteArray();
            var dtbytes = DateTimeToBytes(timestamp);

            // Overwrite the first six bytes with unix time
            Array.Copy(dtbytes, 0, bytes, 0, NumDateBytes);
            return new Guid(bytes);
        }

        public static DateTimeOffset GetTimestamp(Guid comb)
        {
            var bytes = comb.ToByteArray();
            return BytesToDateTime(bytes);
        }
    }
}
