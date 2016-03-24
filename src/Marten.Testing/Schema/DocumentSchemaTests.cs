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
    public class DocumentSchemaTests : IntegratedFixture
    {
        private readonly IDocumentSchema _schema;

        public DocumentSchemaTests()
        {
            ConnectionSource.CleanBasicDocuments();
            _schema = theStore.Schema;
        }

        [Fact]
        public void can_create_a_new_storage_for_a_document_type_without_subclasses()
        {
            var storage = _schema.StorageFor(typeof(User));
            storage.ShouldNotBeNull();
        }

        [Fact]
        public void can_create_storage_for_a_document_type_with_subclasses()
        {
            _schema.Alter(r =>
            {
                r.For<Squad>().AddSubclass<FootballTeam>().AddSubclass<BaseballTeam>();
            });

            _schema.StorageFor(typeof(Squad)).ShouldNotBeNull();
        }

        [Fact]
        public void can_resolve_mapping_for_subclass_type()
        {
            _schema.Alter(r =>
            {
                r.For<Squad>().AddSubclass<FootballTeam>().AddSubclass<BaseballTeam>();
            });

            var mapping = _schema.MappingFor(typeof(BaseballTeam)).ShouldBeOfType<SubClassMapping>();

            mapping.DocumentType.ShouldBe(typeof(BaseballTeam));

            mapping.Parent.DocumentType.ShouldBe(typeof(Squad));
        }

        [Fact]
        public void can_resolve_document_storage_for_subclass()
        {
            _schema.Alter(r =>
            {
                r.For<Squad>().AddSubclass<FootballTeam>().AddSubclass<BaseballTeam>();
            });

            _schema.StorageFor(typeof(BaseballTeam))
                .ShouldBeOfType<SubClassDocumentStorage<BaseballTeam, Squad>>();
        }

        [Fact]
        public void caches_storage_for_a_document_type()
        {
            _schema.StorageFor(typeof(User))
                .ShouldBeSameAs(_schema.StorageFor(typeof(User)));

            _schema.StorageFor(typeof(Issue))
                .ShouldBeSameAs(_schema.StorageFor(typeof(Issue)));

            _schema.StorageFor(typeof(Company))
                .ShouldBeSameAs(_schema.StorageFor(typeof(Company)));
        }

        [Fact]
        public void generate_ddl()
        {
            _schema.StorageFor(typeof(User));
            _schema.StorageFor(typeof(Issue));
            _schema.StorageFor(typeof(Company));

            var sql = _schema.ToDDL();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_get_next_hi");
            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_user");
            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_issue");
            sql.ShouldContain("CREATE OR REPLACE FUNCTION public.mt_upsert_company");
            sql.ShouldContain("CREATE TABLE public.mt_doc_user");
            sql.ShouldContain("CREATE TABLE public.mt_doc_issue");
            sql.ShouldContain("CREATE TABLE public.mt_doc_company");
        }

        [Fact]
        public void include_the_hilo_table_by_default()
        {
            _schema.StorageFor(typeof(User));
            _schema.StorageFor(typeof(Issue));
            _schema.StorageFor(typeof(Company));

            var sql = _schema.ToDDL();
            sql.ShouldContain(SchemaBuilder.GetSqlScript(_schema.StoreOptions.DatabaseSchemaName, "mt_hilo"));
        }

        [Fact]
        public void do_not_write_event_sql_if_the_event_graph_is_not_active()
        {
            _schema.Events.IsActive.ShouldBeFalse();

            _schema.ToDDL().ShouldNotContain("public.mt_streams");
        }

        [Fact]
        public void do_write_the_event_sql_if_the_event_graph_is_active()
        {
            _schema.Events.AddEventType(typeof(MembersJoined));
            _schema.Events.IsActive.ShouldBeTrue();

            _schema.ToDDL().ShouldContain("public.mt_streams");
        }

        [Fact]
        public void builds_schema_objects_on_the_fly_as_needed()
        {
            _schema.StorageFor(typeof(User)).ShouldNotBeNull();
            _schema.StorageFor(typeof(Issue)).ShouldNotBeNull();
            _schema.StorageFor(typeof(Company)).ShouldNotBeNull();

            var tables = _schema.SchemaTableNames();
            tables.ShouldContain("public.mt_doc_user");
            tables.ShouldContain("public.mt_doc_issue");
            tables.ShouldContain("public.mt_doc_company");

            var functions = _schema.SchemaFunctionNames();
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

                schema.StorageFor(typeof(Examples.User)).ShouldNotBeNull();

                Exception<AmbiguousDocumentTypeAliasesException>.ShouldBeThrownBy(() =>
                {
                    schema.StorageFor(typeof(User));
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
                store.Schema.WriteDDLByType("allsql");
            }

            var fileSystem = new FileSystem();
            var files = fileSystem.FindFiles("allsql", FileSet.Shallow("*.sql")).ToArray();

            files.Select(Path.GetFileName).Where(x => x != "all.sql").OrderBy(x => x)
                .ShouldHaveTheSameElementsAs("company.sql", "issue.sql", "mt_hilo.sql", "user.sql");

            files.Each(file =>
            {
                var contents = fileSystem.ReadStringFromFile(file);

                contents.ShouldContain("CREATE TABLE");
                contents.ShouldContain("CREATE OR REPLACE FUNCTION");
            });
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
                store.Schema.Events.IsActive.ShouldBeFalse();
                store.Schema.WriteDDLByType("allsql");
            }

            var fileSystem = new FileSystem();
            fileSystem.FindFiles("allsql", FileSet.Shallow("*mt_streams.sql"))
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

                _.Events.AddAggregateType<Quest>();

                _.Connection(ConnectionSource.ConnectionString);
            }))
            {
                store.Schema.Events.IsActive.ShouldBeTrue();
                store.Schema.WriteDDLByType("allsql");
            }

            var fileSystem = new FileSystem();
            fileSystem.FindFiles("allsql", FileSet.Shallow("*mt_streams.sql"))
                .Any().ShouldBeTrue();
        }

        [Fact]
        public void resolve_a_document_mapping_for_an_event_type()
        {
            _schema.MappingFor(typeof(RaceStarted)).ShouldBeOfType<EventMapping<RaceStarted>>()
                .DocumentType.ShouldBe(typeof(RaceStarted));
        }

        [Fact]
        public void resolve_storage_for_event_type()
        {
            _schema.StorageFor(typeof(RaceStarted)).ShouldBeOfType<EventMapping<RaceStarted>>()
                .DocumentType.ShouldBe(typeof(RaceStarted));
        }

        [Fact]
        public void resolve_mapping_for_event_stream()
        {
            _schema.MappingFor(typeof(EventStream)).ShouldBeOfType<EventGraph>();
        }

        [Fact]
        public void resolve_storage_for_stream_type()
        {
            _schema.StorageFor(typeof(EventStream)).ShouldBeOfType<EventStreamStorage>();
        }
    }

    public class DocumentSchemaOnOtherSchemaTests : IntegratedFixture
    {
        private readonly IDocumentSchema _schema;

        public DocumentSchemaOnOtherSchemaTests()
        {
            ConnectionSource.CleanBasicDocuments();
            _schema = theStore.Schema;
        }

        protected override void StoreOptions(StoreOptions options)
        {
            options.DatabaseSchemaName = "other";
        }

        [Fact]
        public void generate_ddl()
        {
            _schema.StorageFor(typeof(User));
            _schema.StorageFor(typeof(Issue));
            _schema.StorageFor(typeof(Company));

            var sql = _schema.ToDDL();

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
            _schema.StorageFor(typeof(User));
            _schema.StorageFor(typeof(Issue));
            _schema.StorageFor(typeof(Company));

            var sql = _schema.ToDDL();
            sql.ShouldContain(SchemaBuilder.GetSqlScript(_schema.StoreOptions.DatabaseSchemaName, "mt_hilo"));
        }

        [Fact]
        public void do_not_write_event_sql_if_the_event_graph_is_not_active()
        {
            _schema.Events.IsActive.ShouldBeFalse();

            _schema.ToDDL().ShouldNotContain("public.mt_streams");
        }

        [Fact]
        public void do_write_the_event_sql_if_the_event_graph_is_active()
        {
            _schema.Events.AddEventType(typeof(MembersJoined));
            _schema.Events.IsActive.ShouldBeTrue();

            _schema.ToDDL().ShouldContain("other.mt_streams");
        }

        [Fact]
        public void builds_schema_objects_on_the_fly_as_needed()
        {
            _schema.StorageFor(typeof(User)).ShouldNotBeNull();
            _schema.StorageFor(typeof(Issue)).ShouldNotBeNull();
            _schema.StorageFor(typeof(Company)).ShouldNotBeNull();

            var tables = _schema.SchemaTableNames();
            tables.ShouldContain("other.mt_doc_user");
            tables.ShouldContain("other.mt_doc_issue");
            tables.ShouldContain("other.mt_doc_company");

            var functions = _schema.SchemaFunctionNames();
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

                schema.StorageFor(typeof(Examples.User)).ShouldNotBeNull();

                Exception<AmbiguousDocumentTypeAliasesException>.ShouldBeThrownBy(() =>
                {
                    schema.StorageFor(typeof(User));
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
                store.Schema.WriteDDLByType("allsql");
            }

            var fileSystem = new FileSystem();
            var files = fileSystem.FindFiles("allsql", FileSet.Shallow("*.sql")).ToArray();

            files.Select(Path.GetFileName).Where(x => x != "all.sql").OrderBy(x => x)
                .ShouldHaveTheSameElementsAs("company.sql", "issue.sql", "mt_hilo.sql", "user.sql");

            files.Each(file =>
            {
                var contents = fileSystem.ReadStringFromFile(file);

                contents.ShouldContain("CREATE TABLE");
                contents.ShouldContain("CREATE OR REPLACE FUNCTION");
            });
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
                store.Schema.Events.IsActive.ShouldBeFalse();
                store.Schema.WriteDDLByType("allsql");
            }

            var fileSystem = new FileSystem();
            fileSystem.FindFiles("allsql", FileSet.Shallow("*mt_streams.sql"))
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

                _.Events.AddAggregateType<Quest>();

                _.Connection(ConnectionSource.ConnectionString);
            }))
            {
                store.Schema.Events.IsActive.ShouldBeTrue();
                store.Schema.WriteDDLByType("allsql");
            }

            var fileSystem = new FileSystem();
            fileSystem.FindFiles("allsql", FileSet.Shallow("*mt_streams.sql"))
                .Any().ShouldBeTrue();
        }

        [Fact]
        public void resolve_a_document_mapping_for_an_event_type()
        {
            _schema.MappingFor(typeof(RaceStarted)).ShouldBeOfType<EventMapping<RaceStarted>>()
                .DocumentType.ShouldBe(typeof(RaceStarted));
        }

        [Fact]
        public void resolve_storage_for_event_type()
        {
            _schema.StorageFor(typeof(RaceStarted)).ShouldBeOfType<EventMapping<RaceStarted>>()
                .DocumentType.ShouldBe(typeof(RaceStarted));
        }

        [Fact]
        public void resolve_mapping_for_event_stream()
        {
            _schema.MappingFor(typeof(EventStream)).ShouldBeOfType<EventGraph>();
        }

        [Fact]
        public void resolve_storage_for_stream_type()
        {
            _schema.StorageFor(typeof(EventStream)).ShouldBeOfType<EventStreamStorage>();
        }
    }

    public class DocumentSchemaWithOverridenSchemaTests : IntegratedFixture
    {
        private readonly string _sql;
        private readonly string[] _tables;
        private readonly string[] _functions;
        private readonly IDocumentSchema _schema;

        public DocumentSchemaWithOverridenSchemaTests()
        {
            ConnectionSource.CleanBasicDocuments();

            _schema = theStore.Schema;
            _sql = _schema.ToDDL();

            using (var session = theStore.OpenSession())
            {
                session.Store(new User());
                session.Store(new Issue());
                session.Store(new Company());
                session.SaveChanges();
            }

            _tables = _schema.SchemaTableNames();
            _functions = _schema.SchemaFunctionNames();
        }

        protected override void StoreOptions(StoreOptions options)
        {
            options.MappingFor(typeof(User)).DatabaseSchemaName = "other";
            options.MappingFor(typeof(Issue)).DatabaseSchemaName = "overriden";
            options.MappingFor(typeof(Company));
        }

        [Fact]
        public void include_the_hilo_table_by_default()
        {
            _sql.ShouldContain(SchemaBuilder.GetSqlScript(_schema.StoreOptions.DatabaseSchemaName, "mt_hilo"));
        }

        [Fact]
        public void do_not_write_event_sql_if_the_event_graph_is_not_active()
        {
            _schema.Events.IsActive.ShouldBeFalse();
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
        private readonly string[] _tables;
        private readonly string[] _functions;
        private readonly IDocumentSchema _schema;

        public DocumentSchemaWithOverridenDefaultSchemaAndEventsTests()
        {
            ConnectionSource.CleanBasicDocuments();

            _schema = theStore.Schema;

            _sql = _schema.ToDDL();

            using (var session = theStore.OpenSession())
            {
                session.Store(new User());
                session.Store(new Issue());
                session.Store(new Company());
                session.SaveChanges();
            }

            _tables = _schema.SchemaTableNames();
            _functions = _schema.SchemaFunctionNames();
        }

        protected override void StoreOptions(StoreOptions options)
        {
            options.DatabaseSchemaName = "other";
            options.MappingFor(typeof(User)).DatabaseSchemaName = "yet_another";
            options.MappingFor(typeof(Issue)).DatabaseSchemaName = "overriden";
            options.MappingFor(typeof(Company));
            options.Events.AddEventType(typeof(MembersJoined));
        }

        [Fact]
        public void do_write_the_event_sql_if_the_event_graph_is_active()
        {
            _schema.Events.IsActive.ShouldBeTrue();
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

    public class Race : IAggregate
    {
        public Guid Id { get; set; }
    }

    public class RaceStarted : IEvent
    {
        public Guid Id { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}