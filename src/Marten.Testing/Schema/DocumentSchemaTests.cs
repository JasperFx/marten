using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Events;
using Marten.Schema;
using Marten.Schema.Hierarchies;
using Marten.Testing.Documents;
using Marten.Testing.Events;
using Marten.Testing.Schema.Hierarchies;
using Shouldly;
using StructureMap;
using Xunit;
using Issue = Marten.Testing.Documents.Issue;

namespace Marten.Testing.Schema
{
    [Collection("DefaultSchema")]
    public class DocumentSchemaTests : IntegratedFixture
    {
        private readonly string _binAllsql = AppContext.BaseDirectory.AppendPath("bin", "allsql");
        private readonly string _binAllsql2 = AppContext.BaseDirectory.AppendPath("bin", "allsql2");


        [Fact]
        public void can_create_a_new_storage_for_a_document_type_without_subclasses()
        {
            var storage = theStore.DefaultTenant.StorageFor(typeof(User));
            storage.ShouldNotBeNull();
        }

        [Fact]
        public void can_create_storage_for_a_document_type_with_subclasses()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Squad>().AddSubClass<FootballTeam>().AddSubClass<BaseballTeam>();
            });

            theStore.DefaultTenant.StorageFor(typeof(Squad)).ShouldNotBeNull();
        }

        [Fact]
        public void can_resolve_mapping_for_subclass_type()
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Squad>().AddSubClass<FootballTeam>().AddSubClass<BaseballTeam>();
            });

            var mapping = theStore.DefaultTenant.MappingFor(typeof(BaseballTeam)).ShouldBeOfType<SubClassMapping>();

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

            theStore.DefaultTenant.StorageFor(typeof(BaseballTeam))
                .ShouldBeOfType<SubClassDocumentStorage<BaseballTeam, Squad>>();
        }

        [Fact]
        public void caches_storage_for_a_document_type()
        {
            theStore.DefaultTenant.StorageFor(typeof(User))
                .ShouldBeSameAs(theStore.DefaultTenant.StorageFor(typeof(User)));

            theStore.DefaultTenant.StorageFor(typeof(Issue))
                .ShouldBeSameAs(theStore.DefaultTenant.StorageFor(typeof(Issue)));

            theStore.DefaultTenant.StorageFor(typeof(Company))
                .ShouldBeSameAs(theStore.DefaultTenant.StorageFor(typeof(Company)));
        }

        [Fact]
        public void generate_ddl()
        {
            theStore.DefaultTenant.StorageFor(typeof(User));
            theStore.DefaultTenant.StorageFor(typeof(Issue));
            theStore.DefaultTenant.StorageFor(typeof(Company));
            theStore.DefaultTenant.EnsureStorageExists(typeof(IntDoc));

            var sql = theStore.Schema.ToDDL();

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
            StoreOptions(_ =>
            {
                _.Events.AddEventType(typeof(MembersDeparted));
            });

            var sql = theStore.Schema.ToDDL();

            sql.ShouldContain("CREATE TABLE public.mt_streams");

            // Crude way of checking that it should only be dumped once
            sql.IndexOf("CREATE TABLE public.mt_streams").ShouldBe(sql.LastIndexOf("CREATE TABLE public.mt_streams"));
        }

        [Fact]
        public void do_not_write_event_sql_if_the_event_graph_is_not_active()
        {
            theStore.Events.IsActive.ShouldBeFalse();

            theStore.Schema.ToDDL().ShouldNotContain("public.mt_streams");
        }

        [Fact]
        public void do_write_the_event_sql_if_the_event_graph_is_active()
        {
            theStore.Events.AddEventType(typeof(MembersJoined));
            theStore.Events.IsActive.ShouldBeTrue();

            theStore.Schema.ToDDL().ShouldContain("public.mt_streams");
        }

        [Fact]
        public void builds_schema_objects_on_the_fly_as_needed()
        {
            theStore.DefaultTenant.StorageFor(typeof(User)).ShouldNotBeNull();
            theStore.DefaultTenant.StorageFor(typeof(Issue)).ShouldNotBeNull();
            theStore.DefaultTenant.StorageFor(typeof(Company)).ShouldNotBeNull();

            var tables = theStore.Schema.DbObjects.SchemaTables();
            tables.ShouldContain("public.mt_doc_user");
            tables.ShouldContain("public.mt_doc_issue");
            tables.ShouldContain("public.mt_doc_company");

            var functions = theStore.Schema.DbObjects.Functions();
            functions.ShouldContain("public.mt_upsert_user");
            functions.ShouldContain("public.mt_upsert_issue");
            functions.ShouldContain("public.mt_upsert_company");
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

                schema.StoreOptions.Storage.StorageFor(typeof(Examples.User)).ShouldNotBeNull();

                Exception<AmbiguousDocumentTypeAliasesException>.ShouldBeThrownBy(() =>
                {
                    schema.StoreOptions.Storage.StorageFor(typeof(User));
                });
            }
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
                store.Schema.WriteDDLByType(_binAllsql);
            }

            var fileSystem = new FileSystem();
            var files = fileSystem.FindFiles(_binAllsql, FileSet.Shallow("*.sql")).ToArray();

            files.ShouldNotContain("database_schemas.sql");

            var actuals = files.Select(Path.GetFileName).Where(x => x != "all.sql").OrderBy(x => x);

            actuals
                .ShouldHaveTheSameElementsAs("company.sql", "issue.sql", "system_functions.sql", "transforms.sql", "user.sql");


        }

        [Fact]
        public void can_write_patch_by_type_smoke_test()
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


                store.Schema.WritePatchByType(_binAllsql2);
            }

            var fileSystem = new FileSystem();
            var files = fileSystem.FindFiles(_binAllsql2, FileSet.Shallow("*.sql")).ToArray();

            files.Select(Path.GetFileName).Where(x => x != "all.sql").OrderBy(x => x)
                .ShouldHaveTheSameElementsAs("company.sql", "issue.sql", "system_functions.sql", "transforms.sql", "user.sql");


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

                _.Transforms.LoadFile("get_fullname.js");
            }))
            {
                store.Schema.WriteDDLByType(_binAllsql);
            }

            var filename = _binAllsql.AppendPath("all.sql");

            var lines = new FileSystem().ReadStringFromFile(filename).ReadLines().ToArray();

            lines.ShouldContain("\\i user.sql");
            lines.ShouldContain("\\i company.sql");
            lines.ShouldContain("\\i issue.sql");
            lines.ShouldContain("\\i mt_hilo.sql");
            lines.ShouldContain("\\i eventstore.sql");
            lines.ShouldContain("\\i get_fullname.sql");


        }

        [Fact]
        public void writes_transform_function()
        {
            using (var store = DocumentStore.For(_ =>
            {
                _.RegisterDocumentType<User>();
                _.RegisterDocumentType<Company>();
                _.RegisterDocumentType<Issue>();

                _.Events.AddEventType(typeof(MembersJoined));

                _.Connection(ConnectionSource.ConnectionString);

                _.Transforms.LoadFile("get_fullname.js");
            }))
            {
                store.Schema.WriteDDLByType(_binAllsql);
            }

            var file = _binAllsql.AppendPath("get_fullname.sql");
            var lines = new FileSystem().ReadStringFromFile(file).ReadLines().ToArray();


            lines.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_transform_get_fullname(doc JSONB) RETURNS JSONB AS $$");
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
        }

        [Fact]
        public void resolve_a_document_mapping_for_an_event_type()
        {
            theStore.Events.AddEventType(typeof(RaceStarted));

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

    public class building_id_assignment_for_document_types
    {
        private readonly DocumentStore theStore = TestingDocumentStore.Basic();

        [Fact]
        public void can_build_with_guid_property()
        {
            theStore.DefaultTenant.IdAssignmentFor<User>()
                .ShouldNotBeNull();
        }

        [Fact]
        public void can_build_for_int_and_long_id()
        {
            theStore.DefaultTenant.IdAssignmentFor<IntDoc>().ShouldNotBeNull();
            theStore.DefaultTenant.IdAssignmentFor<LongDoc>().ShouldNotBeNull();
        }

        [Fact]
        public void can_build_for_a_field()
        {
            theStore.DefaultTenant.IdAssignmentFor<StringFieldGuy>()
                .ShouldNotBeNull();
        }

        public class StringFieldGuy
        {
            public string Id;
        }
    }

    [Collection("DefaultSchema")]
    public class DocumentSchemaWithOverridenSchemaTests : IntegratedFixture
    {
        private readonly string _sql;
        private readonly DbObjectName[] _tables;
        private readonly DbObjectName[] _functions;
        private readonly IDocumentSchema _schema;

        public DocumentSchemaWithOverridenSchemaTests()
        {
            // SAMPLE: override_schema_per_table
            StoreOptions(_ =>
            {
                _.Storage.MappingFor(typeof(User)).DatabaseSchemaName = "other";
                _.Storage.MappingFor(typeof(Issue)).DatabaseSchemaName = "overriden";
                _.Storage.MappingFor(typeof(Company));
                _.Storage.MappingFor(typeof(IntDoc));

                // this will tell marten to use the default 'public' schema name.
                _.DatabaseSchemaName = Marten.StoreOptions.DefaultDatabaseSchemaName;
            });
            // ENDSAMPLE

            _schema = theStore.Schema;
            _sql = _schema.ToDDL();

            using (var session = theStore.OpenSession())
            {
                session.Store(new User());
                session.Store(new Issue());
                session.Store(new Company());
                session.SaveChanges();
            }

            _tables = _schema.DbObjects.SchemaTables();
            _functions = _schema.DbObjects.Functions();
        }





        [Fact]
        public void include_the_hilo_table_by_default()
        {
            _sql.ShouldContain("public.mt_hilo");
        }

        [Fact]
        public void do_not_write_event_sql_if_the_event_graph_is_not_active()
        {
            theStore.Events.IsActive.ShouldBeFalse();
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

    public class DocumentSchemaWithOverridenDefaultSchemaAndEventsTests : IntegratedFixture
    {
        private readonly string _sql;
        private readonly DbObjectName[] _tables;
        private readonly DbObjectName[] _functions;
        private readonly IDocumentSchema _schema;

        public DocumentSchemaWithOverridenDefaultSchemaAndEventsTests()
        {
            StoreOptions(_ =>
            {
                // SAMPLE: override_schema_name
                _.DatabaseSchemaName = "other";
                // ENDSAMPLE
                _.Storage.MappingFor(typeof(User)).DatabaseSchemaName = "yet_another";
                _.Storage.MappingFor(typeof(Issue)).DatabaseSchemaName = "overriden";
                _.Storage.MappingFor(typeof(Company));
                _.Events.AddEventType(typeof(MembersJoined));
            });

            _schema = theStore.Schema;

            _sql = _schema.ToDDL();

            using (var session = theStore.OpenSession())
            {
                session.Store(new User());
                session.Store(new Issue());
                session.Store(new Company());
                session.SaveChanges();
            }

            _tables = _schema.DbObjects.SchemaTables();
            _functions = _schema.DbObjects.Functions();
        }


        [Fact]
        public void do_write_the_event_sql_if_the_event_graph_is_active()
        {
            theStore.Events.IsActive.ShouldBeTrue();
            _schema.ToDDL().ShouldContain("other.mt_streams");
        }

        [Fact]
        public void then_the_hilo_function_should_be_generated_in_the_default_schema()
        {
            _sql.ShouldContain("CREATE OR REPLACE FUNCTION other.mt_get_next_hi");
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