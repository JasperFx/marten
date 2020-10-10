using System.IO;
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
        public void can_fill_in_the_version_column()
        {
            var mapping = theStore.Tenancy.Default.MappingFor(typeof(User));
            var table = new DocumentTable(mapping.As<DocumentMapping>());
            table.RemoveColumn(SchemaConstants.VersionColumn);

            var writer = new StringWriter();
            table.Write(theStore.Schema.DdlRules, writer);

            using (var conn = theStore.Tenancy.Default.OpenConnection())
            {
                var cmd = new NpgsqlCommand(writer.ToString());
                conn.Execute(cmd);
            }

            theStore.Tenancy.Default.ResetSchemaExistenceChecks();
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            var actual = theStore.Tenancy.Default.DbObjects.ExistingTableFor(typeof(User));

            actual.HasColumn(SchemaConstants.VersionColumn).ShouldBeTrue();
        }

        [Fact]
        public void can_fill_in_the_dotnettype_column()
        {
            var mapping = theStore.Tenancy.Default.MappingFor(typeof(User));
            var table = new DocumentTable(mapping.As<DocumentMapping>());
            table.RemoveColumn(SchemaConstants.DotNetTypeColumn);

            var writer = new StringWriter();
            table.Write(theStore.Schema.DdlRules, writer);

            using (var conn = theStore.Tenancy.Default.OpenConnection())
            {
                var cmd = new NpgsqlCommand(writer.ToString());
                conn.Execute(cmd);
            }

            theStore.Tenancy.Default.ResetSchemaExistenceChecks();
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            var actual = theStore.Tenancy.Default.DbObjects.ExistingTableFor(typeof(User));

            actual.HasColumn(SchemaConstants.DotNetTypeColumn).ShouldBeTrue();
        }

        [Fact]
        public void can_fill_in_the_lastmodified_column()
        {
            var mapping = theStore.Tenancy.Default.MappingFor(typeof(User));
            var table = new DocumentTable(mapping.As<DocumentMapping>());
            table.RemoveColumn(SchemaConstants.LastModifiedColumn);

            var writer = new StringWriter();
            table.Write(theStore.Schema.DdlRules, writer);

            var cmd = new NpgsqlCommand(writer.ToString());
            using (var conn = theStore.Tenancy.Default.OpenConnection())
            {
                conn.Execute(cmd);
            }

            theStore.Tenancy.Default.ResetSchemaExistenceChecks();
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            var actual = theStore.Tenancy.Default.DbObjects.ExistingTableFor(typeof(User));

            actual.HasColumn(SchemaConstants.LastModifiedColumn).ShouldBeTrue();
        }

    }
}
