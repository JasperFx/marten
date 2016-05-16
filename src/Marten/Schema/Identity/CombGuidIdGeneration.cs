using System;
using System.Collections.Generic;

namespace Marten.Schema.Identity
{
    /// <summary>
    ///     Comb Guid Id Generation. More info http://www.informit.com/articles/article.aspx?p=25862
    /// </summary>
    public class CombGuidIdGeneration : IIdGeneration
    {
        private const int NumDateBytes = 6;
        private const double TicksPerMillisecond = 3d/10d;

        private static readonly DateTime _minCombDate = new DateTime(1900, 1, 1);
        private static readonly DateTime _maxCombDate = _minCombDate.AddDays(ushort.MaxValue);
        public IEnumerable<Type> KeyTypes { get; } = new[] {typeof(Guid)};

        public IIdGenerator<T> Build<T>(IDocumentSchema schema)
        {
            return (IIdGenerator<T>) new GuidIdGenerator(NewGuid);
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
        ///     Returns a new Guid COMB, consisting of a random Guid combined with the current UTC timestamp.
        /// </summary>
        public static Guid NewGuid() => Create(Guid.NewGuid(), DateTime.UtcNow);

        /// <summary>
        ///     Returns a new Guid COMB, consisting of a random Guid combined with the provided timestap.
        /// </summary>
        public static Guid NewGuid(DateTime timestamp) => Create(Guid.NewGuid(), timestamp);

        private static byte[] DateTimeToBytes(DateTime timestamp)
        {
            if (timestamp < _minCombDate)
                throw new ArgumentException($"COMB values only support dates on or after {_minCombDate}");
            if (timestamp > _maxCombDate)
                throw new ArgumentException($"COMB values only support dates through {_maxCombDate}");

            // Convert the time to 300ths of a second. SQL Server uses float math for this before converting to an integer, so this does as well
            // to avoid rounding errors. This is confirmed in MSSQL by SELECT CONVERT(varchar, CAST(CAST(2 as binary(8)) AS datetime), 121),
            // which would return .006 if it were integer math, but it returns .007.
            var ticks = (int) (timestamp.TimeOfDay.TotalMilliseconds*TicksPerMillisecond);
            var days = (ushort) (timestamp - _minCombDate).TotalDays;
            var tickBytes = BitConverter.GetBytes(ticks);
            var dayBytes = BitConverter.GetBytes(days);

            if (BitConverter.IsLittleEndian)
            {
                // x86 platforms store the LEAST significant bytes first, we want the opposite for our arrays
                Array.Reverse(dayBytes);
                Array.Reverse(tickBytes);
            }

            var result = new byte[6];
            Array.Copy(dayBytes, 0, result, 0, 2);
            Array.Copy(tickBytes, 0, result, 2, 4);
            return result;
        }

        private static Guid Create(Guid value, DateTime timestamp)
        {
            var bytes = value.ToByteArray();
            var dtbytes = DateTimeToBytes(timestamp);
            // Nybble 6-9 move left to 5-8. Nybble 9 is set to "4" (the version)
            dtbytes[2] = (byte) ((byte) (dtbytes[2] << 4) | (byte) (dtbytes[3] >> 4));
            dtbytes[3] = (byte) ((byte) (dtbytes[3] << 4) | (byte) (dtbytes[4] >> 4));
            dtbytes[4] = (byte) (0x40 | (byte) (dtbytes[4] & 0x0F));
            // Overwrite the first six bytes
            Array.Copy(dtbytes, 0, bytes, 0, NumDateBytes);
            return new Guid(bytes);
        }
    }
}