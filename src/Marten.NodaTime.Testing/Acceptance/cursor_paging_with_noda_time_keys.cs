using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Marten.Linq.CursorPaging;
using Marten.Newtonsoft;
using Marten.Services.Json;
using Marten.NodaTimePlugin.Testing.TestData;
using Marten.Testing.Harness;
using NodaTime;
using Shouldly;
using Xunit;

namespace Marten.NodaTimePlugin.Testing.Acceptance;

public class cursor_paging_with_noda_time_keys: OneOffConfigurationsContext
{
    // The cursor carries the sort key of the last row. It has to be written and read back with the
    // store's serializer: the default System.Text.Json options know nothing about NodaTime, so a
    // LocalDate key came back as 0001-01-01 and every later page repeated the first one.
    [Theory]
    [InlineData(SerializerType.SystemTextJson)]
    [InlineData(SerializerType.Newtonsoft)]
    public async Task pages_do_not_repeat_when_the_sort_key_is_a_noda_time_type(SerializerType serializerType)
    {
        StoreOptions(opts =>
        {
            if (serializerType == SerializerType.Newtonsoft)
            {
                opts.UseNewtonsoftForSerialization();
            }

            opts.UseNodaTime();
        });

        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(TargetWithDates));

        var firstDay = new LocalDate(2026, 9, 1);
        var docs = Enumerable.Range(0, 5).Select(i =>
        {
            var doc = TargetWithDates.Generate();
            doc.LocalDate = firstDay.PlusDays(i);
            return doc;
        }).ToArray();

        await using (var session = theStore.LightweightSession())
        {
            session.Insert(docs);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var query = theStore.QuerySession();

        var ids = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await query.Query<TargetWithDates>()
                .OrderBy(x => x.LocalDate).ThenBy(x => x.Id)
                .ToJsonPageByCursorAsync(cursor, pageSize: 2, TestContext.Current.CancellationToken);

            using var items = JsonDocument.Parse(page.ItemsJson);
            ids.AddRange(items.RootElement.EnumerateArray().Select(x => x.GetProperty("Id").GetGuid()));

            // Three pages cover five rows. Anything more is the first page coming back again.
            ids.Count.ShouldBeLessThanOrEqualTo(docs.Length);

            cursor = page.NextCursor;
        } while (cursor != null);

        ids.ShouldBe(docs.Select(x => x.Id));
    }
}
