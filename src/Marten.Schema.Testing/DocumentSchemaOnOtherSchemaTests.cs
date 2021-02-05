using System;
using System.Linq;
using Baseline;
using Marten.Events;
using Marten.Exceptions;
using Marten.Schema.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Issue = Marten.Schema.Testing.Documents.Issue;

namespace Marten.Schema.Testing
{
    public class DocumentSchemaOnOtherSchemaTests : IntegrationContext
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
            theStore.Tenancy.Default.StorageFor<User>();
            theStore.Tenancy.Default.StorageFor<Issue>();
            theStore.Tenancy.Default.StorageFor<Company>();



            var sql = theSchema.ToDDL();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION other.mt_upsert_user");
            sql.ShouldContain("CREATE OR REPLACE FUNCTION other.mt_upsert_issue");
            sql.ShouldContain("CREATE OR REPLACE FUNCTION other.mt_upsert_company");
            sql.ShouldContain("CREATE TABLE other.mt_doc_user");
            sql.ShouldContain("CREATE TABLE other.mt_doc_issue");
            sql.ShouldContain("CREATE TABLE other.mt_doc_company");
        }


        [Fact]
        public void do_not_write_event_sql_if_the_event_graph_is_not_active()
        {
            theStore.Events.IsActive(null).ShouldBeFalse();

            theSchema.ToDDL().ShouldNotContain("other.mt_streams");
        }

        [Fact]
        public void do_write_the_event_sql_if_the_event_graph_is_active()
        {
            theStore.Events.AddEventType(typeof(MembersJoined));
            theStore.Events.IsActive(null).ShouldBeTrue();

            theSchema.ToDDL().ShouldContain("other.mt_streams");
        }

        [Fact]
        public void builds_schema_objects_on_the_fly_as_needed()
        {
            theStore.Tenancy.Default.StorageFor<User>().ShouldNotBeNull();
            theStore.Tenancy.Default.StorageFor<Issue>().ShouldNotBeNull();
            theStore.Tenancy.Default.StorageFor<Company>().ShouldNotBeNull();

            var tables = theStore.Tenancy.Default.DbObjects.SchemaTables();
            tables.ShouldContain("other.mt_doc_user");
            tables.ShouldContain("other.mt_doc_issue");
            tables.ShouldContain("other.mt_doc_company");

            var functions = theStore.Tenancy.Default.DbObjects.Functions();
            functions.ShouldContain("other.mt_upsert_user");
            functions.ShouldContain("other.mt_upsert_issue");
            functions.ShouldContain("other.mt_upsert_company");
        }

        [Fact]
        public void do_not_rebuild_a_table_that_already_exists()
        {
            // This usage of DocumentStore.For() is okay
            using (var store = DocumentStore.For(ConnectionSource.ConnectionString))
            {
                using (var session = store.LightweightSession())
                {
                    session.Store(new User());
                    session.Store(new User());
                    session.Store(new User());

                    session.SaveChanges();
                }
            }

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
            }))
            {
                using (var session = store.LightweightSession())
                {
                    session.Query<User>().Count().ShouldBeGreaterThanOrEqualTo(3);
                }
            }

        }

        [Fact]
        public void throw_ambigous_alias_exception_when_you_have_duplicate_document_aliases()
        {
            using (var store = DocumentStore.For(ConnectionSource.ConnectionString))
            {

                store.Options.Providers.StorageFor<User>().ShouldNotBeNull();


                Exception<AmbiguousDocumentTypeAliasesException>.ShouldBeThrownBy(() =>
                {
                    store.Options.Providers.StorageFor<User2>().ShouldNotBeNull();
                });
            }
        }

        [DocumentAlias("user")]
        public class User2
        {
            public int Id { get; set; }
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
                _.EventGraph.EventMappingFor<MembersJoined>();

                _.Connection(ConnectionSource.ConnectionString);
            }))
            {
                store.Schema.WriteDDLByType(_binAllsql);
            }

            string filename = _binAllsql.AppendPath("all.sql");

            var fileSystem = new FileSystem();
            fileSystem.FileExists(filename).ShouldBeTrue();

            var contents = fileSystem.ReadStringFromFile(filename);

            SpecificationExtensions.ShouldContain(contents, "CREATE SCHEMA event_store");
            SpecificationExtensions.ShouldContain(contents, "CREATE SCHEMA other");
            SpecificationExtensions.ShouldContain(contents, "CREATE SCHEMA yet_another");
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
                store.Options.Storage.MappingFor(typeof(User))
                    .DatabaseSchemaName.ShouldBe("other");

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
                store.Events.IsActive(null).ShouldBeFalse();
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
                store.Events.IsActive(null).ShouldBeTrue();
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
                store.Events.IsActive(null).ShouldBeTrue();
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

            theStore.Storage.FindMapping(typeof(RaceStarted)).ShouldBeOfType<EventMapping<RaceStarted>>()
                .DocumentType.ShouldBe(typeof(RaceStarted));
        }

        [Fact]
        public void resolve_storage_for_event_type()
        {
            theStore.Events.AddEventType(typeof(RaceStarted));

            theStore.Tenancy.Default.StorageFor<RaceStarted>().ShouldBeOfType<EventMapping<RaceStarted>>()
                .DocumentType.ShouldBe(typeof(RaceStarted));
        }


    }
}
