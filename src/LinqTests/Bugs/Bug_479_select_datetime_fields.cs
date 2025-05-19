using System;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;

namespace LinqTests.Bugs;

public class Bug_479_select_datetime_fields: BugIntegrationContext
{
    public Bug_479_select_datetime_fields()
    {
        StoreOptions(_ => _.UseNewtonsoftForSerialization(EnumStorage.AsString));
    }

    [Fact]
    public async Task select_date_time_as_utc()
    {
        var doc = new DocWithDates
        {
            DateTime = DateTime.Today
        };

        using var session = theStore.LightweightSession();
        session.Store(doc);
        await session.SaveChangesAsync();

        var date = session.Query<DocWithDates>().Where(x => x.Id == doc.Id).Select(x => new { Date = x.DateTime }).Single();

        date.Date.ShouldBe(doc.DateTime);
    }

    [Fact]
    public async Task select_date_time_offset()
    {
        var doc = new DocWithDates
        {
            DateTimeOffset = new DateTimeOffset(2016, 8, 15, 18, 0, 0, 0.Hours())
        };

        using var session = theStore.LightweightSession();
        session.Store(doc);
        await session.SaveChangesAsync();

        var date = session.Query<DocWithDates>().Where(x => x.Id == doc.Id).Select(x => new { Date = x.DateTimeOffset }).Single();

        date.Date.ShouldBe(doc.DateTimeOffset);
    }

    public class DocWithDates
    {
        public Guid Id = Guid.NewGuid();

        public DateTime DateTime;
        public DateTimeOffset DateTimeOffset;

        public DateTime? NullableDateTime;
        public DateTimeOffset? NullableDateTimeOffset;
    }
}
