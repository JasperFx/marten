using System;
using System.Threading.Tasks;
using Marten.Testing.Linq.Compatibility.Support;
using Xunit;

namespace Marten.Testing.Linq.Compatibility
{
    public class simple_where_clauses: LinqTestContext<DefaultQueryFixture, simple_where_clauses>
    {
        public simple_where_clauses(DefaultQueryFixture fixture) : base(fixture)
        {
        }

        static simple_where_clauses()
        {
            @where(x => x.Number == 1);
            @where(x => x.Number > 3);
            @where(x => x.Number < 3);
            @where(x => x.Number <= 3);
            @where(x => x.Number >= 3);
            @where(x => x.Number != 3);
            @where(x => x.Number.Equals(3));
            @where(x => !x.Number.Equals(3));

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
}
