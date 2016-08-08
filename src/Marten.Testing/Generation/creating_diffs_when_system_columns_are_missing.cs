using System.IO;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Util;
using Xunit;

namespace Marten.Testing.Generation
{
    public class creating_diffs_when_system_columns_are_missing : IntegratedFixture
    {
        [Fact]
        public void can_fill_in_the_version_column()
        {
            var mapping = theStore.Schema.MappingFor(typeof(User));
            var table = mapping.SchemaObjects.StorageTable();
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

            theStore.Schema.EnsureStorageExists(typeof(User));

            var actual = theStore.Schema.DbObjects.TableSchema(mapping);

            actual.HasColumn(DocumentMapping.VersionColumn).ShouldBeTrue();
        }

        [Fact]
        public void can_fill_in_the_dotnettype_column()
        {
            var mapping = theStore.Schema.MappingFor(typeof(User));
            var table = mapping.SchemaObjects.StorageTable();
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

            theStore.Schema.EnsureStorageExists(typeof(User));

            var actual = theStore.Schema.DbObjects.TableSchema(mapping);

            actual.HasColumn(DocumentMapping.DotNetTypeColumn).ShouldBeTrue();
        }

        [Fact]
        public void can_fill_in_the_lastmodified_column()
        {
            var mapping = theStore.Schema.MappingFor(typeof(User));
            var table = mapping.SchemaObjects.StorageTable();
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

            theStore.Schema.EnsureStorageExists(typeof(User));

            var actual = theStore.Schema.DbObjects.TableSchema(mapping);

            actual.HasColumn(DocumentMapping.LastModifiedColumn).ShouldBeTrue();

           
        }
    }
}