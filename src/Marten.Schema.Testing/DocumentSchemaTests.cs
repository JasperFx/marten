using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baseline;
using Marten.Events;
using Marten.Internal.Storage;
using Marten.Schema.Testing.Documents;
using Marten.Schema.Testing.Hierarchies;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Xunit;

namespace Marten.Schema.Testing
{
    public class DocumentSchemaTests: IntegrationContext
    {
        private readonly string _binAllsql = AppContext.BaseDirectory.AppendPath("bin", "allsql");
        private readonly string _binAllsql2 = AppContext.BaseDirectory.AppendPath("bin", "allsql2");


        [Fact]
        public void can_create_a_new_storage_for_a_document_type_without_subclasses()
        {
            var storage = theStore.Tenancy.Default.StorageFor<User>();
            storage.ShouldNotBeNull();
        }

        [Fact]
        public void can_create_storage_for_a_document_type_with_subclasses()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Squad>().AddSubClass<FootballTeam>().AddSubClass<BaseballTeam>();
            });

            theStore.Tenancy.Default.StorageFor<Squad>().ShouldNotBeNull();
        }

        [Fact]
        public void can_resolve_mapping_for_subclass_type()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Squad>().AddSubClass<FootballTeam>().AddSubClass<BaseballTeam>();
            });

            var mapping = theStore.Storage.FindMapping(typeof(BaseballTeam)).ShouldBeOfType<SubClassMapping>();

            mapping.DocumentType.ShouldBe(typeof(BaseballTeam));

            mapping.Parent.DocumentType.ShouldBe(typeof(Squad));
        }

        [Fact]
        public void can_resolve_document_storage_for_subclass()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Squad>().AddSubClass<FootballTeam>().AddSubClass<BaseballTeam>();
            });

            theStore.Tenancy.Default.StorageFor<BaseballTeam>()
                .ShouldBeOfType<SubClassDocumentStorage<BaseballTeam, Squad, string>>();
        }

        [Fact]
        public void caches_storage_for_a_document_type()
        {
            theStore.Tenancy.Default.StorageFor<User>()
                .ShouldBeSameAs(theStore.Tenancy.Default.StorageFor<User>());

            theStore.Tenancy.Default.StorageFor<Issue>()
                .ShouldBeSameAs(theStore.Tenancy.Default.StorageFor<Issue>());

            theStore.Tenancy.Default.StorageFor<Company>()
                .ShouldBeSameAs(theStore.Tenancy.Default.StorageFor<Company>());
        }

        [Fact]
        public void generate_ddl()
        {
            theStore.Tenancy.Default.StorageFor<User>();
            theStore.Tenancy.Default.StorageFor<Issue>();
            theStore.Tenancy.Default.StorageFor<Company>();
            theStore.Tenancy.Default.EnsureStorageExists(typeof(IntDoc));

            var sql = theStore.Schema.ToDatabaseScript();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_get_next_hi");
            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_user");
            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_issue");
            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_company");
            sql.ShouldContain("CREATE TABLE public.mt_doc_user");
            sql.ShouldContain("CREATE TABLE public.mt_doc_issue");
            sql.ShouldContain("CREATE TABLE public.mt_doc_company");
        }

        [Fact]
        public void generate_the_ddl_with_the_event_store()
        {
            var schemaName = StoreOptions(_ =>
            {
                _.Events.AddEventType(typeof(MembersDeparted));
            });

            var sql = theStore.Schema.ToDatabaseScript();

            sql.ShouldContain("CREATE TABLE public.mt_streams");

            // Crude way of checking that it should only be dumped once
            sql.IndexOf("CREATE TABLE public.mt_streams").ShouldBe(sql.LastIndexOf("CREATE TABLE public.mt_streams"));
        }

        [Fact]
        public void do_not_write_event_sql_if_the_event_graph_is_not_active()
        {
            // Need to force an empty document store here
            StoreOptions(x => { });

            theStore.Events.IsActive(null).ShouldBeFalse();

            theStore.Schema.ToDatabaseScript().ShouldNotContain("public.mt_streams");
        }

        [Fact]
        public void do_write_the_event_sql_if_the_event_graph_is_active()
        {
            theStore.Events.AddEventType(typeof(MembersJoined));
            theStore.Events.IsActive(null).ShouldBeTrue();

            theStore.Schema.ToDatabaseScript().ShouldContain("public.mt_streams");
        }

        [Fact]
        public async Task builds_schema_objects_on_the_fly_as_needed()
        {
            theStore.Tenancy.Default.StorageFor<User>().ShouldNotBeNull();
            theStore.Tenancy.Default.StorageFor<Issue>().ShouldNotBeNull();
            theStore.Tenancy.Default.StorageFor<Company>().ShouldNotBeNull();

            var tables = (await theStore.Tenancy.Default.SchemaTables()).Select(x => x.QualifiedName).ToArray();
            tables.ShouldContain("public.mt_doc_user");
            tables.ShouldContain("public.mt_doc_issue");
            tables.ShouldContain("public.mt_doc_company");

            var functions = (await theStore.Tenancy.Default.Functions()).Select(x => x.QualifiedName).ToArray();
            functions.ShouldContain("public.mt_upsert_user");
            functions.ShouldContain("public.mt_upsert_issue");
            functions.ShouldContain("public.mt_upsert_company");
        }


        [Fact]
        public void can_write_ddl_by_type_smoke_test()
        {
            using (var store = DocumentStore.For(_ =>
            {
                _.RegisterDocumentType<User>();
                _.RegisterDocumentType<Company>();
                _.RegisterDocumentType<Issue>();

                _.Connection(ConnectionSource.ConnectionString);
            }))
            {
                store.Schema.WriteDatabaseCreationScriptByType(_binAllsql);
            }

            var fileSystem = new FileSystem();
            var files = fileSystem.FindFiles(_binAllsql, FileSet.Shallow("*.sql")).ToArray();

            files.ShouldNotContain("database_schemas.sql");

            var actuals = files.Select(Path.GetFileName).Where(x => x != "all.sql").OrderBy(x => x);

            actuals
                .ShouldHaveTheSameElementsAs("company.sql", "issue.sql", "system_functions.sql",
                    "user.sql");
        }

        [Fact]
        public async Task can_write_patch_by_type_smoke_test()
        {
            using (var store = DocumentStore.For(_ =>
            {
                _.RegisterDocumentType<User>();
                _.RegisterDocumentType<Company>();
                _.RegisterDocumentType<Issue>();

                _.Connection(ConnectionSource.ConnectionString);
            }))
            {
                store.Advanced.Clean.CompletelyRemoveAll();


                await store.Schema.WriteMigrationFileByType(_binAllsql2);
            }

            var fileSystem = new FileSystem();
            var files = fileSystem.FindFiles(_binAllsql2, FileSet.Shallow("*.sql")).ToArray();

            files.Select(Path.GetFileName).Where(x => x != "all.sql").OrderBy(x => x)
                .ShouldHaveTheSameElementsAs("company.sql", "issue.sql", "system_functions.sql",
                    "user.sql");
        }

        [Fact]
        public void write_ddl_by_type_generates_the_all_sql_script()
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
                store.Schema.WriteDatabaseCreationScriptByType(_binAllsql);
            }

            var filename = _binAllsql.AppendPath("all.sql");

            var lines = new FileSystem().ReadStringFromFile(filename).ReadLines().ToArray();

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
                store.Schema.WriteDatabaseCreationScriptByType(_binAllsql);
            }

            var fileSystem = new FileSystem();
            fileSystem.FindFiles(_binAllsql, FileSet.Shallow("*mt_streams.sql"))
                .Any().ShouldBeFalse();
        }

        [Fact]
        public void can_write_ddl_by_type_with_no_database_connection()
        {
            using (var store = DocumentStore.For(_ =>
            {
                _.RegisterDocumentType<User>();
                _.RegisterDocumentType<Company>();
                _.RegisterDocumentType<Issue>();

                _.AutoCreateSchemaObjects = AutoCreate.None;

                _.Connection("");
            }))
            {
                store.Events.IsActive(null).ShouldBeFalse();
                store.Schema.WriteDatabaseCreationScriptByType(_binAllsql);
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
                store.Schema.WriteDatabaseCreationScriptByType(_binAllsql);
            }

            var fileSystem = new FileSystem();
            fileSystem.FindFiles(_binAllsql, FileSet.Shallow("eventstore.sql"))
                .Any().ShouldBeTrue();
        }

        [Fact]
        public void resolve_a_document_mapping_for_an_event_type()
        {
            theStore.Events.AddEventType(typeof(RaceStarted));

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


    [Collection("DefaultSchema")]
    public class DocumentSchemaWithOverridenSchemaTests: IntegrationContext
    {
        private readonly DbObjectName[] _functions;
        private readonly IDocumentSchema _schema;
        private readonly string _sql;
        private readonly DbObjectName[] _tables;

        public DocumentSchemaWithOverridenSchemaTests()
        {
            #region sample_override_schema_per_table

            StoreOptions(_ =>
            {
                _.Storage.MappingFor(typeof(User)).DatabaseSchemaName = "other";
                _.Storage.MappingFor(typeof(Issue)).DatabaseSchemaName = "overriden";
                _.Storage.MappingFor(typeof(Company));
                _.Storage.MappingFor(typeof(IntDoc));

                // this will tell marten to use the default 'public' schema name.
                _.DatabaseSchemaName = SchemaConstants.DefaultSchema;
            });

            #endregion sample_override_schema_per_table

            _schema = theStore.Schema;
            _sql = _schema.ToDatabaseScript();

            using (var session = theStore.OpenSession())
            {
                session.Store(new User());
                session.Store(new Issue());
                session.Store(new Company());
                session.SaveChanges();
            }

            _tables = theStore.Tenancy.Default.SchemaTables().GetAwaiter().GetResult().ToArray();
            _functions = theStore.Tenancy.Default.Functions().GetAwaiter().GetResult().ToArray();
        }


        [Fact]
        public void include_the_hilo_table_by_default()
        {
            _sql.ShouldContain("public.mt_hilo");
        }

        [Fact]
        public void do_not_write_event_sql_if_the_event_graph_is_not_active()
        {
            theStore.Events.IsActive(null).ShouldBeFalse();
            _sql.ShouldNotContain("public.mt_streams");
        }

        [Fact]
        public void then_the_hilo_function_should_be_generated_in_the_default_schema()
        {
            _sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_get_next_hi");
        }

        [Fact]
        public void then_the_user_function_should_be_generated_in_the_default_schema()
        {
            _sql.ShouldContain("CREATE OR REPLACE FUNCTION other.mt_upsert_user");
        }

        [Fact]
        public void then_the_issue_function_should_be_generated_in_the_overriden_schema()
        {
            _sql.ShouldContain("CREATE OR REPLACE FUNCTION overriden.mt_upsert_issue");
        }

        [Fact]
        public void then_the_company_function_should_be_generated_in_the_default_schema()
        {
            _sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_company");
        }

        [Fact]
        public void then_the_user_table_should_be_generated_in_the_other_schema()
        {
            _sql.ShouldContain("CREATE TABLE other.mt_doc_user");
        }

        [Fact]
        public void then_the_issue_table_should_be_generated_in_the_overriden_schema()
        {
            _sql.ShouldContain("CREATE TABLE overriden.mt_doc_issue");
        }

        [Fact]
        public void then_company_table_should_be_generated_in_the_default()
        {
            _sql.ShouldContain("CREATE TABLE public.mt_doc_company");
        }

        [Fact]
        public void then_the_user_table_should_be_generated()
        {
            _tables.ShouldContain("other.mt_doc_user");
        }

        [Fact]
        public void then_the_issue_table_should_be_generated()
        {
            _tables.ShouldContain("overriden.mt_doc_issue");
        }

        [Fact]
        public void then_the_company_table_should_be_generated()
        {
            _tables.ShouldContain("public.mt_doc_company");
        }

        [Fact]
        public void then_the_user_function_should_be_generated()
        {
            _functions.ShouldContain("other.mt_upsert_user");
        }

        [Fact]
        public void then_the_issue_function_should_be_generated()
        {
            _functions.ShouldContain("overriden.mt_upsert_issue");
        }

        [Fact]
        public void then_the_company_function_should_be_generated()
        {
            _functions.ShouldContain("public.mt_upsert_company");
        }

        [Fact]
        public void the_user_should_be_stored()
        {
            using (var session = theStore.QuerySession())
            {
                session.Query<User>().Count().ShouldBe(1);
            }
        }

        [Fact]
        public void the_issue_should_be_stored()
        {
            using (var session = theStore.QuerySession())
            {
                session.Query<Issue>().Count().ShouldBe(1);
            }
        }

        [Fact]
        public void the_company_should_be_stored()
        {
            using (var session = theStore.QuerySession())
            {
                session.Query<Company>().Count().ShouldBe(1);
            }
        }
    }

    public class DocumentSchemaWithOverridenDefaultSchemaAndEventsTests: IntegrationContext
    {
        private readonly DbObjectName[] _functions;
        private readonly IDocumentSchema _schema;
        private readonly string _sql;
        private readonly DbObjectName[] _tables;

        public DocumentSchemaWithOverridenDefaultSchemaAndEventsTests()
        {
            StoreOptions(_ =>
            {
                #region sample_override_schema_name

                _.DatabaseSchemaName = "other";

                #endregion sample_override_schema_name

                _.Storage.MappingFor(typeof(User)).DatabaseSchemaName = "yet_another";
                _.Storage.MappingFor(typeof(Issue)).DatabaseSchemaName = "overriden";
                _.Storage.MappingFor(typeof(Company));
                _.Events.AddEventType(typeof(MembersJoined));
            });

            _schema = theStore.Schema;

            _sql = _schema.ToDatabaseScript();

            using (var session = theStore.OpenSession())
            {
                session.Store(new User());
                session.Store(new Issue());
                session.Store(new Company());
                session.SaveChanges();
            }

            _tables = theStore.Tenancy.Default.SchemaTables().GetAwaiter().GetResult().ToArray();
            _functions = theStore.Tenancy.Default.Functions().GetAwaiter().GetResult().ToArray();
        }


        [Fact]
        public void do_write_the_event_sql_if_the_event_graph_is_active()
        {
            theStore.Events.IsActive(null).ShouldBeTrue();
            _schema.ToDatabaseScript().ShouldContain("other.mt_streams");
        }


        [Fact]
        public void then_the_user_function_should_be_generated_in_the_default_schema()
        {
            _sql.ShouldContain("CREATE OR REPLACE FUNCTION yet_another.mt_upsert_user");
        }

        [Fact]
        public void then_the_issue_function_should_be_generated_in_the_overriden_schema()
        {
            _sql.ShouldContain("CREATE OR REPLACE FUNCTION overriden.mt_upsert_issue");
        }

        [Fact]
        public void then_the_company_function_should_be_generated_in_the_default_schema()
        {
            _sql.ShouldContain("CREATE OR REPLACE FUNCTION other.mt_upsert_company");
        }

        [Fact]
        public void then_the_user_table_should_be_generated_in_the_default_schema()
        {
            _sql.ShouldContain("CREATE TABLE yet_another.mt_doc_user");
        }

        [Fact]
        public void then_the_issue_table_should_be_generated_in_the_overriden_schema()
        {
            _sql.ShouldContain("CREATE TABLE overriden.mt_doc_issue");
        }

        [Fact]
        public void then_company_table_should_be_generated_in_the_default()
        {
            _sql.ShouldContain("CREATE TABLE other.mt_doc_company");
        }

        [Fact]
        public void then_the_user_table_should_be_generated()
        {
            _tables.ShouldContain("yet_another.mt_doc_user");
        }

        [Fact]
        public void then_the_issue_table_should_be_generated()
        {
            _tables.ShouldContain("overriden.mt_doc_issue");
        }

        [Fact]
        public void then_the_company_table_should_be_generated()
        {
            _tables.ShouldContain("other.mt_doc_company");
        }

        [Fact]
        public void then_the_user_function_should_be_generated()
        {
            _functions.ShouldContain("yet_another.mt_upsert_user");
        }

        [Fact]
        public void then_the_issue_function_should_be_generated()
        {
            _functions.ShouldContain("overriden.mt_upsert_issue");
        }

        [Fact]
        public void then_the_company_function_should_be_generated()
        {
            _functions.ShouldContain("other.mt_upsert_company");
        }

        [Fact]
        public void the_user_should_be_stored()
        {
            using (var session = theStore.QuerySession())
            {
                session.Query<User>().Count().ShouldBe(1);
            }
        }

        [Fact]
        public void the_issue_should_be_stored()
        {
            using (var session = theStore.QuerySession())
            {
                session.Query<Issue>().Count().ShouldBe(1);
            }
        }

        [Fact]
        public void the_company_should_be_stored()
        {
            using (var session = theStore.QuerySession())
            {
                session.Query<Company>().Count().ShouldBe(1);
            }
        }
    }

    public class Race
    {
        public Guid Id { get; set; }
    }

    public class RaceStarted
    {
        public Guid Id { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
