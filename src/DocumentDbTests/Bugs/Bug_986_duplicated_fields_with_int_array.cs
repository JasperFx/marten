using System;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_986_duplicated_fields_with_int_array: IntegrationContext
{
    [Fact]
    public async Task can_insert_new_docs()
    {
        var guyWithIntArray = new GuyWithIntArray { Numbers = new[] { 1, 3, 5, 7 } };

        using (var session = theStore.LightweightSession())
        {
            session.Store(guyWithIntArray);
            await session.SaveChangesAsync();
        }

        using (var session = theStore.QuerySession())
        {
            (await session.LoadAsync<GuyWithIntArray>(guyWithIntArray.Id)).Numbers.ShouldHaveTheSameElementsAs(1, 3, 5, 7);
        }
    }

    public Bug_986_duplicated_fields_with_int_array(DefaultStoreFixture fixture) : base(fixture)
    {
    }
}

public class GuyWithIntArray
{
    public Guid Id { get; set; }

    [DuplicateField]
    public int[] Numbers { get; set; }
}
