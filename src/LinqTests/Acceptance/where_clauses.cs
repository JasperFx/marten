using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LinqTests.Acceptance.Support;
using Xunit.Abstractions;

namespace LinqTests.Acceptance;

public class where_clauses: LinqTestContext<where_clauses>
{
    public where_clauses(DefaultQueryFixture fixture, ITestOutputHelper output) : base(fixture)
    {
        TestOutput = output;
    }

    static where_clauses()
    {
        @where(x => x.Number == 1);
        @where(x => x.Number > 3);
        @where(x => x.Number < 3);
        @where(x => x.Number <= 3);
        @where(x => x.Number >= 3);
        @where(x => x.Number != 3);
        @where(x => x.Number.Equals(3));
        @where(x => !x.Number.Equals(3));

        // Using constants
        @where(x => 2 == x.Number);
        @where(x => 1 < x.Number);

        @where(x => null == x.NullableNumber);
        @where(x => x.Number == 1 || 2 == x.Number);

        var num = 2;
        @where(x => num == x.Number);

        @where(x => x.Long == 1);
        @where(x => x.Long > 3);
        @where(x => x.Long < 3);
        @where(x => x.Long <= 3);
        @where(x => x.Long >= 3);
        @where(x => x.Long != 3);
        @where(x => x.Long.Equals(3));
        @where(x => !x.Long.Equals(3));

        @where(x => x.String == "A");
        @where(x => x.String.Equals("a", StringComparison.OrdinalIgnoreCase));
        @where(x => string.Equals(x.String, "a", StringComparison.OrdinalIgnoreCase));
        @where(x => string.Equals("a", x.String, StringComparison.OrdinalIgnoreCase));
        @where(x => string.Equals(x.String, null, StringComparison.OrdinalIgnoreCase));
        @where(x => x.String.Equals("A", StringComparison.Ordinal));
        @where(x => x.String != "A");

        @where(x => x.String.Equals("A"));
        @where(x => !x.String.Equals("A"));

        @where(x => x.String == "A" && x.Number == 1);
        @where(x => x.String == "A" || x.Number == 1);

        @where(x => x.String.Contains("B"));
        @where(x => x.String.Contains("b", StringComparison.OrdinalIgnoreCase));
        @where(x => x.String.StartsWith("Bar"));
        @where(x => x.String.StartsWith("bar", StringComparison.OrdinalIgnoreCase));
        @where(x => x.String.EndsWith("Foo"));
        @where(x => x.String.EndsWith("foo", StringComparison.OrdinalIgnoreCase));

        @where(x => x.String == null);

        @where(x => x.Flag);
        @where(x => x.Flag.Equals(true));
        @where(x => !x.Flag.Equals(true));
        @where(x => x.Flag.Equals(false));
        @where(x => !x.Flag.Equals(false));
        @where(x => x.Flag == true);
        @where(x => !x.Flag);
        @where(x => x.Flag == false);

        @where(x => x.Inner.Flag);
        @where(x => !x.Inner.Flag);
        @where(x => x.Inner.Flag == true);
        @where(x => x.Inner.Flag == false);

        @where(x => x.Double == 10);
        @where(x => x.Double != 10);
        @where(x => x.Double > 10);
        @where(x => x.Double < 10);
        @where(x => x.Double <= 10);
        @where(x => x.Double >= 10);
        @where(x => x.Double.Equals(10));
        @where(x => !x.Double.Equals(10));

        @where(x => x.Decimal == 10);
        @where(x => x.Decimal != 10);
        @where(x => x.Decimal > 10);
        @where(x => x.Decimal < 10);
        @where(x => x.Decimal <= 10);
        @where(x => x.Decimal >= 10);
        @where(x => x.Decimal.Equals(10));
        @where(x => !x.Decimal.Equals(10));

        var today = DateTime.Today;

        @where(x => x.Date == today);
        @where(x => x.Date != today);
        @where(x => x.Date > today);
        @where(x => x.Date < today);
        @where(x => x.Date >= today);
        @where(x => x.Date <= today);
        @where(x => x.Date.Equals(today));
        @where(x => !x.Date.Equals(today));

        @where(x => !(x.Number == 1));
        @where(x => !(x.Number > 3));
        @where(x => !(x.Number < 3));
        @where(x => !(x.Number <= 3));
        @where(x => !(x.Number >= 3));
        @where(x => !(x.Number != 3));


        @where(x => false && x.Number == 1);
        @where(x => true && x.Number == 1);

        @where(x => x.Number > x.AnotherNumber);

        var numbers = new List<int> { 1, 2, 3 };
        IList<int> numbers2 = new List<int> { 1, 2, 3 };
        @where(x => numbers.Contains(x.Number));
        @where(x => numbers2.Contains(x.Number));

        @where(x => x.Inner == null);
        @where(x => x.Inner != null);

        @where(x => x.Flag);
        @where(x => x.Flag == true);
        @where(x => x.Flag == false);
        @where(x => !x.Flag);
        @where(x => !x.Flag == true);
        @where(x => !x.Flag == false);

        // Comparing multiple fields
        @where(x => x.Number == x.AnotherNumber);
        @where(x => x.Number < x.AnotherNumber);
        @where(x => x.Number > x.AnotherNumber);
        @where(x => x.Number <= x.AnotherNumber);
        @where(x => x.Number >= x.AnotherNumber);


        // Dictionaries
        @where(x => x.StringDict.ContainsKey("key0"));

        var kvp = new KeyValuePair<string, string>("key0", "value0");
        @where(x => x.StringDict.Contains(kvp));
        @where(x => x.StringDict.Values.Contains("value3"));
        @where(x => x.StringDict.Keys.Contains("key2"));
        @where(x => x.StringDict.Values.Any(v => v.EndsWith("3")));
        @where(x => x.StringDict.Keys.Any(v => v.EndsWith("3")));
        @where(x => x.StringDict.Any());
        @where(x => !x.StringDict.Any());
        @where(x => x.StringDict.Any(p => p.Key == "key1"));
        @where(x => x.StringDict.Any(p => p.Value == "value2"));

        @where(x => x.StringDict.Count > 2);
        @where(x => x.StringDict.Count() == 2);


        @where(x => x.NumberArray != null && x.NumberArray.Length > 1 && x.NumberArray[1] == 3);
        @where(x => x.StringArray != null && x.StringArray.Length > 2 && x.StringArray[2] == "Red");

        @where(x => x.String.ToLower() == "red");
        @where(x => x.String.ToUpper() == "RED");
    }

    [Theory]
    [MemberData(nameof(GetDescriptions))]
    public Task run_query(string description)
    {
        return assertTestCase(description, Fixture.Store);
    }

    [Theory]
    [MemberData(nameof(GetDescriptions))]
    public Task with_duplicated_fields(string description)
    {
        return assertTestCase(description, Fixture.DuplicatedFieldStore);
    }
}
