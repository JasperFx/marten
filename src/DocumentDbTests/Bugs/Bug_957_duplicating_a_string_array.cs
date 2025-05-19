using System;
using JasperFx;
using Marten.Testing.Harness;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_957_duplicating_a_string_array: BugIntegrationContext
{
    [Fact]
    public void works_just_fine_on_the_first_cut()
    {
        theStore.Tenancy.Default.Database.EnsureStorageExists(typeof(HistoryDoc));

        var store2 = SeparateStore(_ =>
        {
            _.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
            _.Schema.For<HistoryDoc>().Duplicate(x => x.UrlHistory, pgType: "text[]");
        });

        store2.Tenancy.Default.Database.EnsureStorageExists(typeof(HistoryDoc));
    }

}

public class HistoryDoc
{
    public Guid Id { get; set; }
    public string[] UrlHistory { get; set; }
}
