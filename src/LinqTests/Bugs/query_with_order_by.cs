using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class query_with_order_by: IntegrationContext
{
    public query_with_order_by(DefaultStoreFixture fixture): base(fixture)
    {
    }

    [Fact]
    public async Task query_with_order_by_for_string_property_with_comparer()
    {
        await CreateTestData();

        await RunTest(StringComparer.InvariantCultureIgnoreCase, true);
        await RunTest(StringComparer.OrdinalIgnoreCase, true);

        await RunTest(StringComparer.InvariantCulture, false);
        await RunTest(StringComparer.Ordinal, false);

        async Task RunTest(StringComparer comparer, bool shouldBeCaseInsensitive)
        {
            var query = theSession.Query<Target>()
                .OrderBy(x => x.String, comparer);

            var sql = (await query.ExplainAsync()).Command.CommandText;
            var result = query.ToList();

            if (shouldBeCaseInsensitive)
            {
                result[0].String.ShouldBe("A");
                result[1].String.ShouldBe("b");
                result[2].String.ShouldBe("C");

                sql.ShouldContain("lower", Case.Insensitive);
            }
            else
            {
                // Ordering without lower() is dependant on PostgreSQL version/configuration, so order is not asserted here
                sql.ShouldNotContain("lower", Case.Insensitive);
            }
        }
    }

    [Fact]
    public async Task query_with_order_by_descending_for_string_property_with_comparer()
    {
        await CreateTestData();

        await RunTest(StringComparer.InvariantCultureIgnoreCase, true);
        await RunTest(StringComparer.OrdinalIgnoreCase, true);

        await RunTest(StringComparer.InvariantCulture, false);
        await RunTest(StringComparer.Ordinal, false);

        async Task RunTest(StringComparer comparer, bool shouldBeCaseInsensitive)
        {
            var query = theSession.Query<Target>()
                .OrderByDescending(x => x.String, comparer);

            var sql = (await query.ExplainAsync()).Command.CommandText;
            var result = query.ToList();

            if (shouldBeCaseInsensitive)
            {
                result[0].String.ShouldBe("C");
                result[1].String.ShouldBe("b");
                result[2].String.ShouldBe("A");

                sql.ShouldContain("lower", Case.Insensitive);
            }
            else
            {
                // Ordering without lower() is dependant on PostgreSQL version/configuration, so order is not asserted here
                sql.ShouldNotContain("lower", Case.Insensitive);
            }
        }
    }

    [Fact]
    public void query_by_property_name_and_string_comparer()
    {
        theSession.Query<Target>().OrderBy("String", StringComparer.Ordinal)
            .ToCommand()
            .CommandText.ShouldBe("select d.id, d.data from public.mt_doc_target as d order by d.data ->> 'String';");

        theSession.Query<Target>().OrderBy("String", StringComparer.OrdinalIgnoreCase)
            .ToCommand()
            .CommandText.ShouldBe("select d.id, d.data from public.mt_doc_target as d order by lower(d.data ->> 'String');");
    }

    [Fact]
    public async Task query_with_then_by_for_string_property_with_comparer()
    {
        await CreateTestData(true);

        await RunTest(StringComparer.InvariantCultureIgnoreCase, true);
        await RunTest(StringComparer.OrdinalIgnoreCase, true);

        await RunTest(StringComparer.InvariantCulture, false);
        await RunTest(StringComparer.Ordinal, false);

        async Task RunTest(StringComparer comparer, bool shouldBeCaseInsensitive)
        {
            var query = theSession.Query<Target>()
                .OrderBy(x => x.Number)
                .ThenBy(x => x.String, comparer);

            var sql = (await query.ExplainAsync()).Command.CommandText;
            var result = query.ToList();

            if (shouldBeCaseInsensitive)
            {
                result[0].String.ShouldBe("A");
                result[1].String.ShouldBe("b");
                result[2].String.ShouldBe("C");

                result[3].String.ShouldBe("A");
                result[4].String.ShouldBe("b");
                result[5].String.ShouldBe("C");

                sql.ShouldContain("lower", Case.Insensitive);
            }
            else
            {
                // Ordering without lower() is dependant on PostgreSQL version/configuration, so order is not asserted here
                sql.ShouldNotContain("lower", Case.Insensitive);
            }
        }
    }

    [Fact]
    public async Task query_with_then_by_descending_for_string_property_with_comparer()
    {
        await CreateTestData(true);

        await RunTest(StringComparer.InvariantCultureIgnoreCase, true);
        await RunTest(StringComparer.OrdinalIgnoreCase, true);

        await RunTest(StringComparer.InvariantCulture, false);
        await RunTest(StringComparer.Ordinal, false);

        async Task RunTest(StringComparer comparer, bool shouldBeCaseInsensitive)
        {
            var query = theSession.Query<Target>()
                .OrderBy(x => x.Number)
                .ThenByDescending(x => x.String, comparer);

            var sql = (await query.ExplainAsync()).Command.CommandText;
            var result = query.ToList();

            if (shouldBeCaseInsensitive)
            {
                result[0].String.ShouldBe("C");
                result[1].String.ShouldBe("b");
                result[2].String.ShouldBe("A");

                result[3].String.ShouldBe("C");
                result[4].String.ShouldBe("b");
                result[5].String.ShouldBe("A");

                sql.ShouldContain("lower", Case.Insensitive);
            }
            else
            {
                // Ordering without lower() is dependant on PostgreSQL version/configuration, so order is not asserted here
                sql.ShouldNotContain("lower", Case.Insensitive);
            }
        }
    }

    private async Task CreateTestData(bool createTargetsWithNumberTwo = false)
    {
        theSession.Store(new Target
        {
            String = "C",
            Number = 1
        });

        theSession.Store(new Target
        {
            String = "A",
            Number = 1
        });

        theSession.Store(new Target
        {
            String = "b",
            Number = 1
        });

        if (createTargetsWithNumberTwo)
        {
            theSession.Store(new Target
            {
                String = "C",
                Number = 2
            });

            theSession.Store(new Target
            {
                String = "A",
                Number = 2
            });

            theSession.Store(new Target
            {
                String = "b",
                Number = 2
            });
        }

        await theSession.SaveChangesAsync();
    }
}
