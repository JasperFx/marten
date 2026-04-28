using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using JasperFx.Core;
using Weasel.Postgresql;
using Marten.Schema;
using Newtonsoft.Json.Linq;
using Shouldly;
using Weasel.Core;

namespace Marten.Testing.Harness
{
    public static class SpecificationExtensions
    {
        public static void ShouldBeSemanticallySameJsonAs(this string json, string expectedJson)
        {
            var actual = JToken.Parse(json);
            var expected = JToken.Parse(expectedJson);

            JToken.DeepEquals(expected, actual).ShouldBeTrue($"Expected:\n{expectedJson}\nGot:\n{json}");
        }

        public static void ShouldHaveTheSameElementsAs<T>(this IEnumerable<T> actual, IEnumerable<T> expected)
        {
            var actualList = (actual is IList tempActual) ? tempActual : actual.ToList();
            var expectedList = (expected is IList tempExpected) ? tempExpected : expected.ToList();

            ShouldHaveTheSameElementsAs(actualList, expectedList);
        }

        public static void ShouldHaveTheSameElementsAs<T>(this IEnumerable<T> actual, params T[] expected)
        {
            var actualList = (actual is IList tempActual) ? tempActual : actual.ToList();
            var expectedList = (expected is IList tempExpected) ? tempExpected : expected.ToList();

            ShouldHaveTheSameElementsAs(actualList, expectedList);
        }

        public static void ShouldHaveTheSameElementsAs(this IList actual, IList expected)
        {
            actual.ShouldNotBeNull();
            expected.ShouldNotBeNull();

            try
            {
                actual.Count.ShouldBe(expected.Count);

                for (var i = 0; i < actual.Count; i++)
                {
                    actual[i].ShouldBe(expected[i]);
                }
            }
            catch (Exception)
            {
                Debug.WriteLine("ACTUAL:");
                foreach (var o in actual)
                {
                    Debug.WriteLine(o);
                }
                throw;
            }
        }

        public static void ShouldBeEqualWithDbPrecision(this DateTimeOffset actual, DateTimeOffset expected)
        {
            // PostgreSQL `timestamptz` is microsecond precision; .NET DateTimeOffset
            // is 100ns precision. A round-trip through Postgres truncates up to 9
            // ticks. Compare with a millisecond tolerance — easily wider than the
            // truncation but tight enough to catch real semantic differences. This
            // is preferred over the older "round both sides to 100µs and compare"
            // approach because it's more robust to clock-comparison edge cases on
            // slow / loaded CI runners (see #4310).
            var difference = (actual - expected).Duration();
            difference.ShouldBeLessThanOrEqualTo(TimeSpan.FromMilliseconds(1),
                $"DateTimeOffset values differ by {difference.TotalMicroseconds:F1}µs; expected within 1ms.\n" +
                $"  expected: {expected:O}\n" +
                $"    actual: {actual:O}");
        }

        public static void ShouldContain(this DbObjectName[] names, string qualifiedName)
        {
            ArgumentNullException.ThrowIfNull(names);

            var function = DbObjectName.Parse(PostgresqlProvider.Instance, qualifiedName);
            names.ShouldContain(function);
        }
    }

}
