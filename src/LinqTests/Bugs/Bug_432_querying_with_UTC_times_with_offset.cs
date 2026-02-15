using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
namespace LinqTests.Bugs;

public class Bug_432_querying_with_UTC_times_with_offset: BugIntegrationContext
{

    [Fact]
    public async Task can_issue_queries_against_DateTime()
    {
        using (var session = theStore.LightweightSession())
        {
            var now = GenerateTestDateTime();

            var testClass = new DateClass
            {
                Id = Guid.NewGuid(),
                DateTimeField = now
            };

            session.Store(testClass);

            session.Store(new DateClass
            {
                DateTimeField = now.Add(5.Minutes())
            });

            session.Store(new DateClass
            {
                DateTimeField = now.Add(-5.Minutes())
            });

            await session.SaveChangesAsync();


            session.Query<DateClass>()
                .Count(x => now >= x.DateTimeField).ShouldBe(2);
        }
    }

    [Fact]
    public async Task can_issue_queries_against_DateTime_with_camel_casing()
    {
        StoreOptions(_ => _.UseSystemTextJsonForSerialization(casing: Casing.CamelCase));

        using (var session = theStore.LightweightSession())
        {
            var now = GenerateTestDateTime();

            var testClass = new DateClass
            {
                Id = Guid.NewGuid(),
                DateTimeField = now
            };

            session.Store(testClass);

            session.Store(new DateClass
            {
                DateTimeField = now.Add(5.Minutes())
            });

            session.Store(new DateClass
            {
                DateTimeField = now.Add(-5.Minutes())
            });

            await session.SaveChangesAsync();


            session.Query<DateClass>()
                .Count(x => now >= x.DateTimeField).ShouldBe(2);
        }
    }

    [Fact]
    public async Task can_issue_queries_against_DateTime_with_snake_casing()
    {
        StoreOptions(_ => _.UseSystemTextJsonForSerialization(casing: Casing.SnakeCase));

        using (var session = theStore.LightweightSession())
        {
            var now = GenerateTestDateTime();

            var testClass = new DateClass
            {
                Id = Guid.NewGuid(),
                DateTimeField = now
            };

            session.Store(testClass);

            session.Store(new DateClass
            {
                DateTimeField = now.Add(5.Minutes())
            });

            session.Store(new DateClass
            {
                DateTimeField = now.Add(-5.Minutes())
            });

            await session.SaveChangesAsync();


            session.Query<DateClass>()
                .Count(x => now >= x.DateTimeField).ShouldBe(2);
        }
    }

    [Fact]
    public async Task can_issue_queries_against_DateTime_as_duplicated_column()
    {
        StoreOptions(_ => _.Schema.For<DateClass>().Duplicate(x => x.DateTimeField));

        using (var session = theStore.LightweightSession())
        {
            var now = GenerateTestDateTime();

            var testClass = new DateClass
            {
                Id = Guid.NewGuid(),
                DateTimeField = now
            };

            session.Store(testClass);

            session.Store(new DateClass
            {
                DateTimeField = now.Add(5.Minutes())
            });

            session.Store(new DateClass
            {
                DateTimeField = now.Add(-5.Minutes())
            });

            await session.SaveChangesAsync();



            session.Query<DateClass>()
                .Count(x => now >= x.DateTimeField).ShouldBe(2);
        }
    }

    [Fact]
    public async Task can_issue_queries_against_the_datetime_offset()
    {
        using (var session = theStore.LightweightSession())
        {
            var now = GenerateTestDateTimeOffset();

            var testClass = new DateOffsetClass
            {
                Id = Guid.NewGuid(),
                DateTimeOffsetField = now
            };

            session.Store(testClass);

            session.Store(new DateOffsetClass
            {
                DateTimeOffsetField = now.Add(5.Minutes())
            });

            session.Store(new DateOffsetClass
            {
                DateTimeOffsetField = now.Add(-5.Minutes())
            });

            await session.SaveChangesAsync();



            session.Query<DateOffsetClass>()
                .Count(x => now >= x.DateTimeOffsetField).ShouldBe(2);
        }
    }

    [Fact]
    public async Task can_issue_queries_against_the_datetime_offset_as_duplicate_field()
    {
        StoreOptions(_ => _.Schema.For<DateOffsetClass>().Duplicate(x => x.DateTimeOffsetField));

        using (var session = theStore.LightweightSession())
        {
            var now = GenerateTestDateTimeOffset();

            var testClass = new DateOffsetClass
            {
                Id = Guid.NewGuid(),
                DateTimeOffsetField = now
            };

            session.Store(testClass);

            session.Store(new DateOffsetClass
            {
                DateTimeOffsetField = now.Add(5.Minutes())
            });

            session.Store(new DateOffsetClass
            {
                DateTimeOffsetField = now.Add(-5.Minutes())
            });

            await session.SaveChangesAsync();



            session.Query<DateOffsetClass>()
                .Count(x => now >= x.DateTimeOffsetField).ShouldBe(2);
        }
    }

    private static DateTime GenerateTestDateTime()
    {
        var now = DateTime.Now;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
    }

    private static DateTimeOffset GenerateTestDateTimeOffset()
    {
        var now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
    }
}

public class DateClass
{
    public Guid Id { get; set; }
    public DateTime DateTimeField { get; set; }
}

public class DateOffsetClass
{
    public Guid Id { get; set; }
    public DateTimeOffset DateTimeOffsetField { get; set; }
}
