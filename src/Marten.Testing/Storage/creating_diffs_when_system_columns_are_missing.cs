using System;
using System.IO;
using Baseline;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Util;
using Xunit;

namespace Marten.Testing.Storage
{
    public class creating_diffs_when_system_columns_are_missing : IntegratedFixture
    {
        [Fact]
        public void can_fill_in_the_version_column()
        {
            var mapping = theStore.DefaultTenant.MappingFor(typeof(User));
            var table = new DocumentTable(mapping.As<DocumentMapping>());
            table.RemoveColumn(DocumentMapping.VersionColumn);

            var writer = new StringWriter();
            table.Write(theStore.Schema.StoreOptions.DdlRules, writer);

            using (var conn = theStore.Advanced.OpenConnection())
            {
                conn.Execute(cmd =>
                {
                    cmd.Sql(writer.ToString()).ExecuteNonQuery();
                });
            }

            theStore.DefaultTenant.ResetSchemaExistenceChecks();
            theStore.DefaultTenant.EnsureStorageExists(typeof(User));

            var actual = theStore.Schema.DbObjects.ExistingTableFor(typeof(User));

            actual.HasColumn(DocumentMapping.VersionColumn).ShouldBeTrue();
        }

        [Fact]
        public void can_fill_in_the_dotnettype_column()
        {
            var mapping = theStore.DefaultTenant.MappingFor(typeof(User));
            var table = new DocumentTable(mapping.As<DocumentMapping>());
            table.RemoveColumn(DocumentMapping.DotNetTypeColumn);

            var writer = new StringWriter();
            table.Write(theStore.Schema.StoreOptions.DdlRules, writer);

            using (var conn = theStore.Advanced.OpenConnection())
            {
                conn.Execute(cmd =>
                {
                    cmd.Sql(writer.ToString()).ExecuteNonQuery();
                });
            }

            theStore.DefaultTenant.ResetSchemaExistenceChecks();
            theStore.DefaultTenant.EnsureStorageExists(typeof(User));



            var actual = theStore.Schema.DbObjects.ExistingTableFor(typeof(User));

            actual.HasColumn(DocumentMapping.DotNetTypeColumn).ShouldBeTrue();
        }

        [Fact]
        public void can_fill_in_the_lastmodified_column()
        {
            var mapping = theStore.DefaultTenant.MappingFor(typeof(User));
            var table = new DocumentTable(mapping.As<DocumentMapping>());
            table.RemoveColumn(DocumentMapping.LastModifiedColumn);

            var writer = new StringWriter();
            table.Write(theStore.Schema.StoreOptions.DdlRules, writer);

            using (var conn = theStore.Advanced.OpenConnection())
            {
                conn.Execute(cmd =>
                {
                    cmd.Sql(writer.ToString()).ExecuteNonQuery();
                });
            }

            theStore.DefaultTenant.ResetSchemaExistenceChecks();
            theStore.DefaultTenant.EnsureStorageExists(typeof(User));

            var actual = theStore.Schema.DbObjects.ExistingTableFor(typeof(User));

            actual.HasColumn(DocumentMapping.LastModifiedColumn).ShouldBeTrue();


        }
    }
}