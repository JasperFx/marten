using System.Threading.Tasks;
using Marten.Testing;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Postgresql;
using Xunit;

namespace DocumentDbTests.Configuration;

public class ability_to_add_custom_pg_type_fetch_data: OneOffConfigurationsContext
{
    [Fact]
    public async Task can_register_a_custom_feature_and_reload_types()
    {
        await using (var conn = theStore.CreateConnection())
        {
            await conn.OpenAsync();
            var cmd = conn.CreateCommand(
                $"DROP TYPE IF EXISTS {SchemaName}.mood;CREATE TYPE {SchemaName}.mood AS ENUM ('sad', 'ok', 'happy');");
            await cmd.ExecuteNonQueryAsync();
        }

        await theStore.Advanced.ReloadTypes();

        await using (var conn = theStore.CreateConnection())
        {
            await conn.OpenAsync();
            var cmd = conn.CreateCommand($"select 'happy'::{SchemaName}.mood");
            await using var reader = await cmd.ExecuteReaderAsync();
            reader.Read();
            reader.GetValue(0).ShouldBe("happy");
        }
    }
}
