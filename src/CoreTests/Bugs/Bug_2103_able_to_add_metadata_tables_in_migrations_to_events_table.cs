using System;
using System.Threading.Tasks;
using CoreTests.Diagnostics;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Events;
using Marten.Testing.Harness;
using Weasel.Core;
using Xunit;

namespace CoreTests.Bugs;

public class Bug_2103_able_to_add_metadata_tables_in_migrations_to_events_table : BugIntegrationContext
{
    [Fact]
    public async Task should_add_new_metadata_table()
    {
        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.AutoCreateSchemaObjects = AutoCreate.None;
            opts.Events.AddEventType(typeof(AEvent));
        }, true);

        await theStore.As<IDocumentStore>().Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        StoreOptions(opts =>
        {
            opts.Events.StreamIdentity = StreamIdentity.AsString;
            opts.AutoCreateSchemaObjects = AutoCreate.None;
            opts.Events.AddEventType(typeof(AEvent));
            opts.Events.MetadataConfig.HeadersEnabled = true;
        }, false);

        var martenStorage = theStore.As<IDocumentStore>().Storage;
        await martenStorage.ApplyAllConfiguredChangesToDatabaseAsync();
        await martenStorage.Database.AssertDatabaseMatchesConfigurationAsync();

        theSession.Events.Append(Guid.NewGuid().ToString(), new QuestStarted());
        await theSession.SaveChangesAsync();

    }
}
