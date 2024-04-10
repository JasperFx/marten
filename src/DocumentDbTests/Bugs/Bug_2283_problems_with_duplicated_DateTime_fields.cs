using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_2283_problems_with_duplicated_DateTime_fields: BugIntegrationContext
{
    [Fact]
    public async Task cannot_query_on_duplicated_datetime_field()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<MyModel>().Duplicate(x => x.Date);
            opts.UseDefaultSerialization();
        });
        var model1 = new MyModel { UserId = Guid.NewGuid(), Date = DateTime.Now };

        theSession.Store(model1);
        await theSession.SaveChangesAsync();
        var value = await theSession
            .Query<MyModel>()
            .Where(t => t.UserId == model1.UserId)
            .MinAsync(t => t.Date);
    }

    [Fact]
    public async Task can_query_on_duplicated_datetimeoffset_field()
    {
        StoreOptions(opts =>
        {
            opts.Schema.For<MyModel>().Duplicate(x => x.Date);
            opts.UseDefaultSerialization();
        });
        var model1 = new MyModel { UserId = Guid.NewGuid(), DateTimeOffset = DateTimeOffset.UtcNow };

        theSession.Store(model1);
        await theSession.SaveChangesAsync();

        var minDate = await theSession
            .Query<MyModel>()
            .Where(t => t.UserId == model1.UserId)
            .MinAsync(t => t.DateTimeOffset);

        minDate.ShouldBeEqualWithDbPrecision(model1.DateTimeOffset);
    }
}

public class MyModel
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }

    public DateTime Date { get; set; }

    public DateTimeOffset DateTimeOffset { get; set; }
}
