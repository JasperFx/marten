using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Events.Bugs
{
    public class Bug_443_erroneously_creating_the_event_tables_when_unnecessary: BugIntegrationContext
    {
        [Fact]
        public async Task event_table_should_not_be_there_if_unused()
        {
            theStore.Tenancy.Default.EnsureStorageExists(typeof(User));

            await theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            var tables = await theStore.Tenancy.Default.SchemaTables();

            tables.Any(x => x.Name == "mt_events").ShouldBeFalse();
            tables.Any(x => x.Name == "mt_streams").ShouldBeFalse();
        }

        [Fact]
        public void event_tables_are_not_part_of_the_ddl()
        {
            var ddl = theStore.Schema.ToDatabaseScript();

            SpecificationExtensions.ShouldNotContain(ddl, "mt_events");
            SpecificationExtensions.ShouldNotContain(ddl, "mt_streams");
        }

        [Fact]
        public async Task not_part_of_the_patch()
        {
            var patch = await theStore.Schema.CreateMigration();

            SpecificationExtensions.ShouldNotContain(patch.UpdateSql, "mt_events");
            SpecificationExtensions.ShouldNotContain(patch.UpdateSql, "mt_streams");
        }

        [Fact]
        public void not_part_of_the_by_type_dump()
        {
            var directory = AppContext.BaseDirectory.AppendPath("sql");
            var fileSystem = new FileSystem();
            fileSystem.CleanDirectory(directory);

            theStore.Schema.WriteDatabaseCreationScriptByType(directory);

            fileSystem.FindFiles(directory, FileSet.Shallow("*.sql"))
                .Any(x => x.EndsWith("mt_streams.sql"))
                .ShouldBeFalse();
        }

    }
}
