using System.IO;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema.Testing.Documents;
using Marten.Storage;
using Marten.Util;
using Npgsql;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing.Storage
{
    public class creating_diffs_when_system_columns_are_missing : IntegrationContext
    {
        [Fact]
        public async Task can_fill_in_the_version_column()
        {
            var mapping = theStore.Storage.MappingFor(typeof(User));
            var table = new DocumentTable(mapping.As<DocumentMapping>());
            table.RemoveColumn(SchemaConstants.VersionColumn);

            var writer = new StringWriter();
            table.WriteCreateStatement(theStore.Schema.DdlRules, writer);

            using (var conn = theStore.Tenancy.Default.OpenConnection())
            {
                var cmd = new NpgsqlCommand(writer.ToString());
                await conn.ExecuteAsync(cmd);
            }

            theStore.Tenancy.Default.ResetSchemaExistenceChecks();
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            var actual = await theStore.Tenancy.Default.ExistingTableFor(typeof(User));

            actual.HasColumn(SchemaConstants.VersionColumn).ShouldBeTrue();
        }

        [Fact]
        public async Task can_fill_in_the_dotnettype_column()
        {
            var mapping = theStore.Storage.MappingFor(typeof(User));
            var table = new DocumentTable(mapping.As<DocumentMapping>());
            table.RemoveColumn(SchemaConstants.DotNetTypeColumn);

            var writer = new StringWriter();
            table.WriteCreateStatement(theStore.Schema.DdlRules, writer);

            using (var conn = theStore.Tenancy.Default.OpenConnection())
            {
                var cmd = new NpgsqlCommand(writer.ToString());
                await conn.ExecuteAsync(cmd);
            }

            theStore.Tenancy.Default.ResetSchemaExistenceChecks();
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            var actual = await theStore.Tenancy.Default.ExistingTableFor(typeof(User));

            actual.HasColumn(SchemaConstants.DotNetTypeColumn).ShouldBeTrue();
        }

        [Fact]
        public async Task can_fill_in_the_lastmodified_column()
        {
            var mapping = theStore.Storage.MappingFor(typeof(User));
            var table = new DocumentTable(mapping.As<DocumentMapping>());
            table.RemoveColumn(SchemaConstants.LastModifiedColumn);

            var writer = new StringWriter();
            table.WriteCreateStatement(theStore.Schema.DdlRules, writer);

            var cmd = new NpgsqlCommand(writer.ToString());
            using (var conn = theStore.Tenancy.Default.OpenConnection())
            {
                await conn.ExecuteAsync(cmd);
            }

            theStore.Tenancy.Default.ResetSchemaExistenceChecks();
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            var actual = await theStore.Tenancy.Default.ExistingTableFor(typeof(User));

            actual.HasColumn(SchemaConstants.LastModifiedColumn).ShouldBeTrue();
        }

    }
}
