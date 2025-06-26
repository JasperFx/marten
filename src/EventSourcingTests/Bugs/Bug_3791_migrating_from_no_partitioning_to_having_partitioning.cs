using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Events;
using Marten;
using Marten.Testing.Harness;
using Weasel.Core;
using Xunit;

namespace EventSourcingTests.Bugs;

public class Bug_3791_migrating_from_no_partitioning_to_having_partitioning : BugIntegrationContext
{
    [Fact]
    public async Task try_it_out()
    {
        StoreOptions(o =>
        {
            //o.Events.UseArchivedStreamPartitioning = true;
            o.DisableNpgsqlLogging = true;
            o.Events.UseOptimizedProjectionRebuilds = true;
            o.GeneratedCodeMode = TypeLoadMode.Auto;
            o.Events.AppendMode = EventAppendMode.Quick;
            o.Events.UseIdentityMapForAggregates = true;
            o.DatabaseSchemaName = "some_custom_schema";
            o.UseSystemTextJsonForSerialization(EnumStorage.AsString, Casing.CamelCase, c =>
            {
                c.PropertyNameCaseInsensitive = true;
            });
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var store2 = SeparateStore(o =>
        {
            o.Events.UseArchivedStreamPartitioning = true;
            o.DisableNpgsqlLogging = true;
            o.Events.UseOptimizedProjectionRebuilds = true;
            o.GeneratedCodeMode = TypeLoadMode.Auto;
            o.Events.AppendMode = EventAppendMode.Quick;
            o.Events.UseIdentityMapForAggregates = true;
            o.DatabaseSchemaName = "some_custom_schema";
            o.UseSystemTextJsonForSerialization(EnumStorage.AsString, Casing.CamelCase, c =>
            {
                c.PropertyNameCaseInsensitive = true;
            });
        });
        
        await store2.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }
}
