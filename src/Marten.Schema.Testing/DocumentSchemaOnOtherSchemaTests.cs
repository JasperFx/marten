using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Events;
using Marten.Exceptions;
using Marten.Schema.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Xunit;
using Issue = Marten.Schema.Testing.Documents.Issue;

namespace Marten.Schema.Testing
{
    public class DocumentSchemaOnOtherSchemaTests : IntegrationContext
    {
        private readonly string _binAllsql = AppContext.BaseDirectory.AppendPath("bin", "allsql");
        private readonly string _binAllsql2 = AppContext.BaseDirectory.AppendPath("bin", "allsql2");

        private IDatabase theSchema => theStore.Schema;

        public DocumentSchemaOnOtherSchemaTests()
        {
            StoreOptions(_ => _.DatabaseSchemaName = "other");
        }


        [Fact]
        public void do_not_write_event_sql_if_the_event_graph_is_not_active()
        {
            theStore.Events.IsActive(null).ShouldBeFalse();

            theSchema.ToDatabaseScript().ShouldNotContain("other.mt_streams");
        }

        [Fact]
        public void do_write_the_event_sql_if_the_event_graph_is_active()
        {
            theStore.Events.AddEventType(typeof(MembersJoined));
            theStore.Events.IsActive(null).ShouldBeTrue();

            theSchema.ToDatabaseScript().ShouldContain("other.mt_streams");
        }

        [Fact]
        public async Task builds_schema_objects_on_the_fly_as_needed()
        {
            theStore.Tenancy.Default.Database.StorageFor<User>().ShouldNotBeNull();
            theStore.Tenancy.Default.Database.StorageFor<Issue>().ShouldNotBeNull();
            theStore.Tenancy.Default.Database.StorageFor<Company>().ShouldNotBeNull();

            var tables = (await theStore.Tenancy.Default.Database.SchemaTables()).Select(x => x.QualifiedName).ToArray();
            tables.ShouldContain("other.mt_doc_user");
            tables.ShouldContain("other.mt_doc_issue");
            tables.ShouldContain("other.mt_doc_company");

            var functions = (await theStore.Tenancy.Default.Database.Functions()).Select(x => x.QualifiedName).ToArray();
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
            using var store = DocumentStore.For(ConnectionSource.ConnectionString);
            store.Options.Providers.StorageFor<User>().ShouldNotBeNull();


            Exception<AmbiguousDocumentTypeAliasesException>.ShouldBeThrownBy(() =>
            {
                store.Options.Providers.StorageFor<User2>().ShouldNotBeNull();
            });
        }

        [DocumentAlias("user")]
        public class User2
        {
            public int Id { get; set; }
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
                store.Schema.WriteDatabaseCreationScriptByType(_binAllsql);
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

            theStore.Tenancy.Default.Database.StorageFor<RaceStarted>().ShouldBeOfType<EventMapping<RaceStarted>>()
                .DocumentType.ShouldBe(typeof(RaceStarted));
        }


    }
}
