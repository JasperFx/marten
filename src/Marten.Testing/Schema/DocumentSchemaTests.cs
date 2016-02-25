using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Events;
using Marten.Schema;
using Marten.Schema.Hierarchies;
using Marten.Testing.Documents;
using Marten.Testing.Schema.Hierarchies;
using Shouldly;
using StructureMap;
using Xunit;

namespace Marten.Testing.Schema
{
    public class DocumentSchemaTests : IDisposable
    {
        private readonly DocumentSchema _schema;
        private readonly IContainer _container = Container.For<DevelopmentModeRegistry>();

        public DocumentSchemaTests()
        {
            ConnectionSource.CleanBasicDocuments();
            _schema = _container.GetInstance<DocumentSchema>();
        }

        public void Dispose()
        {
            _schema.Dispose();
        }

        [Fact]
        public void can_create_a_new_storage_for_a_document_type_without_subclasses()
        {
            var storage = _schema.StorageFor(typeof (User));
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

            var mapping = _schema.MappingFor(typeof (BaseballTeam)).ShouldBeOfType<SubClassMapping>();

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

            _schema.StorageFor(typeof (BaseballTeam))
                .ShouldBeOfType<SubClassDocumentStorage<BaseballTeam, Squad>>();

        }


        [Fact]
        public void caches_storage_for_a_document_type()
        {
            _schema.StorageFor(typeof (User))
                .ShouldBeSameAs(_schema.StorageFor(typeof (User)));

            _schema.StorageFor(typeof (Issue))
                .ShouldBeSameAs(_schema.StorageFor(typeof (Issue)));

            _schema.StorageFor(typeof (Company))
                .ShouldBeSameAs(_schema.StorageFor(typeof (Company)));
        }

        [Fact]
        public void generate_ddl()
        {
            _schema.StorageFor(typeof (User));
            _schema.StorageFor(typeof (Issue));
            _schema.StorageFor(typeof (Company));

            var sql = _schema.ToDDL();

            sql.ShouldContain("CREATE OR REPLACE FUNCTION mt_get_next_hi");
            sql.ShouldContain("CREATE OR REPLACE FUNCTION mt_upsert_user");
            sql.ShouldContain("CREATE OR REPLACE FUNCTION mt_upsert_issue");
            sql.ShouldContain("CREATE OR REPLACE FUNCTION mt_upsert_company");
            sql.ShouldContain("CREATE TABLE mt_doc_user");
            sql.ShouldContain("CREATE TABLE mt_doc_issue");
            sql.ShouldContain("CREATE TABLE mt_doc_company");
        }

        [Fact]
        public void builds_schema_objects_on_the_fly_as_needed()
        {
            _schema.StorageFor(typeof (User)).ShouldNotBeNull();
            _schema.StorageFor(typeof (Issue)).ShouldNotBeNull();
            _schema.StorageFor(typeof (Company)).ShouldNotBeNull();



            var schema = Container.For<DevelopmentModeRegistry>().GetInstance<IDocumentSchema>();
            var tables = schema.SchemaTableNames();
            tables.ShouldContain(schema.MappingFor(typeof(User)).TableName);
            tables.ShouldContain(schema.MappingFor(typeof(Issue)).TableName);
            tables.ShouldContain(schema.MappingFor(typeof(Company)).TableName);

            var functions = schema.SchemaFunctionNames();
            functions.ShouldContain(schema.MappingFor(typeof(User)).As<DocumentMapping>().UpsertName);
            functions.ShouldContain(schema.MappingFor(typeof(Issue)).As<DocumentMapping>().UpsertName);
            functions.ShouldContain(schema.MappingFor(typeof(Company)).As<DocumentMapping>().UpsertName);

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

                schema.StorageFor(typeof (Examples.User)).ShouldNotBeNull();

                Exception<AmbiguousDocumentTypeAliasesException>.ShouldBeThrownBy(() =>
                {
                    schema.StorageFor(typeof (User));
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
                .ShouldHaveTheSameElementsAs("company.sql", "issue.sql", "user.sql");

            files.Each(file =>
            {
                var contents = fileSystem.ReadStringFromFile(file);

                contents.ShouldContain("CREATE TABLE");
                contents.ShouldContain("CREATE OR REPLACE FUNCTION");
            });
        }

        [Fact]
        public void resolve_a_document_mapping_for_an_event_type()
        {
            var schema = new DocumentSchema(new StoreOptions(), null, null);
            schema.MappingFor(typeof(RaceStarted)).ShouldBeOfType<EventMapping>()
                .DocumentType.ShouldBe(typeof(RaceStarted));
        }

        [Fact]
        public void resolve_storage_for_event_type()
        {
            var schema = new DocumentSchema(new StoreOptions(), null, null);
            schema.StorageFor(typeof(RaceStarted)).ShouldBeOfType<EventMapping>()
                .DocumentType.ShouldBe(typeof(RaceStarted));
        }

        [Fact]
        public void resolve_mapping_for_event_stream()
        {
            var schema = new DocumentSchema(new StoreOptions(), null, null);
            schema.MappingFor(typeof (EventStream)).ShouldBeOfType<EventGraph>();
        }

        [Fact]
        public void resolve_storage_for_stream_type()
        {
            var schema = new DocumentSchema(new StoreOptions(), new ConnectionSource(), new DevelopmentSchemaCreation(new ConnectionSource(), new NulloMartenLogger()));
            schema.StorageFor(typeof (EventStream)).ShouldBeOfType<EventStreamStorage>();
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