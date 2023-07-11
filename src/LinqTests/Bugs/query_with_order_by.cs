using System;
using System.Linq;
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
    public void query_with_order_by_for_string_property_with_comparer()
    {
        CreateTestData();

        RunTest(StringComparer.InvariantCultureIgnoreCase, true);
        RunTest(StringComparer.OrdinalIgnoreCase, true);

        RunTest(StringComparer.InvariantCulture, false);
        RunTest(StringComparer.Ordinal, false);

        void RunTest(StringComparer comparer, bool shouldBeCaseInsensitive)
        {
            var query = theSession.Query<Target>()
                .OrderBy(x => x.String, comparer);

            var sql = query.Explain().Command.CommandText;
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
    public void query_with_order_by_descending_for_string_property_with_comparer()
    {
        CreateTestData();

        RunTest(StringComparer.InvariantCultureIgnoreCase, true);
        RunTest(StringComparer.OrdinalIgnoreCase, true);

        RunTest(StringComparer.InvariantCulture, false);
        RunTest(StringComparer.Ordinal, false);

        void RunTest(StringComparer comparer, bool shouldBeCaseInsensitive)
        {
            var query = theSession.Query<Target>()
                .OrderByDescending(x => x.String, comparer);

            var sql = query.Explain().Command.CommandText;
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
    public void query_with_then_by_for_string_property_with_comparer()
    {
        CreateTestData(true);

        RunTest(StringComparer.InvariantCultureIgnoreCase, true);
        RunTest(StringComparer.OrdinalIgnoreCase, true);

        RunTest(StringComparer.InvariantCulture, false);
        RunTest(StringComparer.Ordinal, false);

        void RunTest(StringComparer comparer, bool shouldBeCaseInsensitive)
        {
            var query = theSession.Query<Target>()
                .OrderBy(x => x.Number)
                .ThenBy(x => x.String, comparer);

            var sql = query.Explain().Command.CommandText;
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
    public void query_with_then_by_descending_for_string_property_with_comparer()
    {
        CreateTestData(true);

        RunTest(StringComparer.InvariantCultureIgnoreCase, true);
        RunTest(StringComparer.OrdinalIgnoreCase, true);

        RunTest(StringComparer.InvariantCulture, false);
        RunTest(StringComparer.Ordinal, false);

        void RunTest(StringComparer comparer, bool shouldBeCaseInsensitive)
        {
            var query = theSession.Query<Target>()
                .OrderBy(x => x.Number)
                .ThenByDescending(x => x.String, comparer);

            var sql = query.Explain().Command.CommandText;
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

    private void CreateTestData(bool createTargetsWithNumberTwo = false)
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

        theSession.SaveChanges();
    }
}
