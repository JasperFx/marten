using System;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Events;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Events;
using Shouldly;
using StructureMap;
using Xunit;
using Issue = Marten.Testing.Documents.Issue;

namespace Marten.Testing.Schema
{
    public class DocumentSchemaOnOtherSchemaTests : IntegratedFixture
    {
        private readonly string _binAllsql = AppContext.BaseDirectory.AppendPath("bin", "allsql");
        private readonly string _binAllsql2 = AppContext.BaseDirectory.AppendPath("bin", "allsql2");

        private IDocumentSchema theSchema => theStore.Schema;

        public DocumentSchemaOnOtherSchemaTests()
        {
            StoreOptions(_ => _.DatabaseSchemaName = "other");
        }


        [Fact]
        public void generate_ddl()
        {
            theStore.DefaultTenant.StorageFor(typeof(User));
            theStore.DefaultTenant.StorageFor(typeof(Issue));
            theStore.DefaultTenant.StorageFor(typeof(Company));

            var sql = theSchema.ToDDL();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION other.mt_get_next_hi");
            sql.ShouldContain("CREATE OR REPLACE FUNCTION other.mt_upsert_user");
            sql.ShouldContain("CREATE OR REPLACE FUNCTION other.mt_upsert_issue");
            sql.ShouldContain("CREATE OR REPLACE FUNCTION other.mt_upsert_company");
            sql.ShouldContain("CREATE TABLE other.mt_doc_user");
            sql.ShouldContain("CREATE TABLE other.mt_doc_issue");
            sql.ShouldContain("CREATE TABLE other.mt_doc_company");
        }

        [Fact]
        public void include_the_hilo_table_by_default()
        {
            theStore.DefaultTenant.StorageFor(typeof(User));
            theStore.DefaultTenant.StorageFor(typeof(Issue));
            theStore.DefaultTenant.StorageFor(typeof(Company));

            var sql = theSchema.ToDDL();
            sql.ShouldContain(SchemaBuilder.GetSqlScript("other", "mt_hilo"));
        }

        [Fact]
        public void do_not_write_event_sql_if_the_event_graph_is_not_active()
        {
            theStore.Events.IsActive.ShouldBeFalse();

            theSchema.ToDDL().ShouldNotContain("other.mt_streams");
        }

        [Fact]
        public void do_write_the_event_sql_if_the_event_graph_is_active()
        {
            theStore.Events.AddEventType(typeof(MembersJoined));
            theStore.Events.IsActive.ShouldBeTrue();

            theSchema.ToDDL().ShouldContain("other.mt_streams");
        }

        [Fact]
        public void builds_schema_objects_on_the_fly_as_needed()
        {
            theStore.DefaultTenant.StorageFor(typeof(User)).ShouldNotBeNull();
            theStore.DefaultTenant.StorageFor(typeof(Issue)).ShouldNotBeNull();
            theStore.DefaultTenant.StorageFor(typeof(Company)).ShouldNotBeNull();

            var tables = theSchema.DbObjects.SchemaTables();
            tables.ShouldContain("other.mt_doc_user");
            tables.ShouldContain("other.mt_doc_issue");
            tables.ShouldContain("other.mt_doc_company");

            var functions = theSchema.DbObjects.Functions();
            functions.ShouldContain("other.mt_upsert_user");
            functions.ShouldContain("other.mt_upsert_issue");
            functions.ShouldContain("other.mt_upsert_company");
        }

        [Fact]
        public void do_not_rebuild_a_table_that_already_exists()
        {
            using (var container1 = Container.For<DevelopmentModeRegistry>())
            {
                using (var session = container1.GetInstance<IDocumentStore>().LightweightSession())
                {
                    session.Store(new User());
                    session.Store(new User());
                    session.Store(new User());

                    session.SaveChanges();
                }
            }

            using (var container2 = Container.For<DevelopmentModeRegistry>())
            {
                using (var session = container2.GetInstance<IDocumentStore>().LightweightSession())
                {
                    session.Query<User>().Count().ShouldBeGreaterThanOrEqualTo(3);
                }
            }
        }

        [Fact]
        public void throw_ambigous_alias_exception_when_you_have_duplicate_document_aliases()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                var schema = container.GetInstance<IDocumentSchema>();

                var storage = container.GetInstance<IDocumentStore>().As<DocumentStore>().Storage;

                storage.StorageFor(typeof(Examples.User)).ShouldNotBeNull();

                Exception<AmbiguousDocumentTypeAliasesException>.ShouldBeThrownBy(() =>
                {
                    storage.StorageFor(typeof(User));
                });
            }
        }

        [Fact]
        public void can_write_ddl_by_type_smoke_test_for_document_creation()
        {
            using (var store = DocumentStore.For(_ =>
            {
                _.RegisterDocumentType<User>();
                _.RegisterDocumentType<Company>();
                _.RegisterDocumentType<Issue>();

                _.Connection(ConnectionSource.ConnectionString);
            }))
            {
                store.Schema.WriteDDLByType(_binAllsql);
            }

            var fileSystem = new FileSystem();
            var files = fileSystem.FindFiles(_binAllsql, FileSet.Shallow("*.sql")).ToArray();

            files.Select(Path.GetFileName)
                .Where(x => x != "all.sql" && x != "database_schemas.sql").OrderBy(x => x)
                .ShouldHaveTheSameElementsAs("company.sql", "issue.sql", "mt_hilo.sql", "mt_immutable_timestamp.sql", "patch_doc.sql", "user.sql");

            files.Where(x => !x.Contains("all.sql") && !x.Contains("patch_doc.sql") && !x.Contains("mt_immutable_timestamp.sql")).Each(file =>
            {
                var contents = fileSystem.ReadStringFromFile(file);

                contents.ShouldContain("CREATE TABLE");
                contents.ShouldContain("CREATE OR REPLACE FUNCTION");
            });
        }

        [Fact]
        public void can_write_ddl_by_type_with_schema_creation()
        {
            using (var store = DocumentStore.For(_ =>
            {
                _.DatabaseSchemaName = "yet_another";

                _.RegisterDocumentType<Company>();
                _.Schema.For<User>().DatabaseSchemaName("other");

                _.Events.DatabaseSchemaName = "event_store";
                _.Events.EventMappingFor<MembersJoined>();

                _.Connection(ConnectionSource.ConnectionString);
            }))
            {
                store.Schema.WriteDDLByType(_binAllsql);
            }

            string filename = _binAllsql.AppendPath("all.sql");

            var fileSystem = new FileSystem();
            fileSystem.FileExists(filename).ShouldBeTrue();

            var contents = fileSystem.ReadStringFromFile(filename);
            
            contents.ShouldContain("CREATE SCHEMA event_store");
            contents.ShouldContain("CREATE SCHEMA other");
            contents.ShouldContain("CREATE SCHEMA yet_another");
        }

        [Fact]
        public void write_ddl_by_type_generates_the_all_sql_script()
        {
            using (var store = DocumentStore.For(_ =>
            {
                _.RegisterDocumentType<User>();
                _.RegisterDocumentType<Company>();
                _.RegisterDocumentType<Issue>();

                _.DatabaseSchemaName = "yet_another";
                _.Schema.For<User>().DatabaseSchemaName("other");

                _.Events.AddEventType(typeof(MembersJoined));

                _.Connection(ConnectionSource.ConnectionString);
            }))
            {
                store.Schema.WriteDDLByType(_binAllsql);
            }

            var filename = _binAllsql.AppendPath("all.sql");

            var lines = new FileSystem().ReadStringFromFile(filename).ReadLines().Select(x => x.Trim()).ToArray();

            // should create the schemas too
            lines.ShouldContain("EXECUTE 'CREATE SCHEMA yet_another';");
            lines.ShouldContain("EXECUTE 'CREATE SCHEMA other';");

            lines.ShouldContain("\\i user.sql");
            lines.ShouldContain("\\i company.sql");
            lines.ShouldContain("\\i issue.sql");
            lines.ShouldContain("\\i mt_hilo.sql");
            lines.ShouldContain("\\i eventstore.sql");
        }


        [Fact]
        public void write_ddl_by_type_with_no_events()
        {
            using (var store = DocumentStore.For(_ =>
            {
                _.RegisterDocumentType<User>();
                _.RegisterDocumentType<Company>();
                _.RegisterDocumentType<Issue>();

                _.Connection(ConnectionSource.ConnectionString);
            }))
            {
                store.Events.IsActive.ShouldBeFalse();
                store.Schema.WriteDDLByType(_binAllsql);
            }

            var fileSystem = new FileSystem();
            fileSystem.FindFiles(_binAllsql, FileSet.Shallow("*mt_streams.sql"))
                .Any().ShouldBeFalse();
        }

        [Fact]
        public void write_ddl_by_type_with_events()
        {
            using (var store = DocumentStore.For(_ =>
            {
                _.RegisterDocumentType<User>();
                _.RegisterDocumentType<Company>();
                _.RegisterDocumentType<Issue>();

                _.Events.AddEventType(typeof(MembersJoined));

                _.Connection(ConnectionSource.ConnectionString);
            }))
            {
                store.Events.IsActive.ShouldBeTrue();
                store.Schema.WriteDDLByType(_binAllsql);
            }

            var fileSystem = new FileSystem();
            fileSystem.FindFiles(_binAllsql, FileSet.Shallow("eventstore.sql"))
                .Any().ShouldBeTrue();

            fileSystem.FindFiles(_binAllsql, FileSet.Shallow(".sql"))
                .Any().ShouldBeFalse();
        }

        [Fact]
        public void fixing_bug_343_double_export_of_events()
        {
            using (var store = DocumentStore.For(_ =>
            {
                _.RegisterDocumentType<User>();
                _.RegisterDocumentType<Company>();
                _.RegisterDocumentType<Issue>();

                _.Events.AddEventType(typeof(MembersJoined));

                _.Connection(ConnectionSource.ConnectionString);
            }))
            {
                store.Events.IsActive.ShouldBeTrue();
                store.Schema.WriteDDLByType(_binAllsql);
            }

            var fileSystem = new FileSystem();
            fileSystem.FindFiles(_binAllsql, FileSet.Shallow(".sql"))
                .Any().ShouldBeFalse();
        }

        [Fact]
        public void resolve_a_document_mapping_for_an_event_type()
        {
            StoreOptions(_ =>
            {
                _.Events.AddEventType(typeof(RaceStarted));
            });

            theStore.DefaultTenant.MappingFor(typeof(RaceStarted)).ShouldBeOfType<EventMapping<RaceStarted>>()
                .DocumentType.ShouldBe(typeof(RaceStarted));
        }

        [Fact]
        public void resolve_storage_for_event_type()
        {
            theStore.Events.AddEventType(typeof(RaceStarted));

            theStore.DefaultTenant.StorageFor(typeof(RaceStarted)).ShouldBeOfType<EventMapping<RaceStarted>>()
                .DocumentType.ShouldBe(typeof(RaceStarted));
        }


    }
}