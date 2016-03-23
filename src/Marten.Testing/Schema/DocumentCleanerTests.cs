using System.Linq;
using Baseline;
using Marten.Generation;
using Marten.Schema;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Fixtures;
using Shouldly;
using StructureMap;
using Xunit;

namespace Marten.Testing.Schema
{
    public class DocumentCleanerTests : DocumentSessionFixture<NulloIdentityMap>
    {
        private readonly DocumentCleaner theCleaner;

        public DocumentCleanerTests()
        {
            theCleaner = theContainer.GetInstance<DocumentCleaner>();
        }

        [Fact]
        public void clean_table()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new Target { Number = 3 });
            theSession.Store(new Target { Number = 4 });
            theSession.Store(new Target { Number = 5 });
            theSession.Store(new Target { Number = 6 });

            theSession.SaveChanges();
            theSession.Dispose();

            theCleaner.DeleteDocumentsFor(typeof(Target));

            using (var session = theStore.QuerySession())
            {
                session.Query<Target>().Count().ShouldBe(0);
            }
        }

        [Fact]
        public void delete_all_documents()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new User());
            theSession.Store(new Company());
            theSession.Store(new Issue());

            theSession.SaveChanges();
            theSession.Dispose();

            theCleaner.DeleteAllDocuments();

            using (var session = theStore.QuerySession())
            {
                session.Query<Target>().Count().ShouldBe(0);
                session.Query<User>().Count().ShouldBe(0);
                session.Query<Issue>().Count().ShouldBe(0);
                session.Query<Company>().Count().ShouldBe(0);
            }

        }

        [Fact]
        public void completely_remove_document_type()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });

            theSession.SaveChanges();
            theSession.Dispose();

            var tableName = theStore.Schema.MappingFor(typeof(Target)).QualifiedTableName;

            var schema = theContainer.GetInstance<DocumentSchema>();
            schema.DocumentTables().Contains(tableName)
                .ShouldBeTrue();

            theCleaner.CompletelyRemove(typeof(Target));

            schema.DocumentTables().Contains(tableName)
                .ShouldBeFalse();
        }

        [Fact]
        public void completely_remove_document_removes_the_upsert_command_too()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });

            theSession.SaveChanges();

            var schema = theContainer.GetInstance<DocumentSchema>();

            var upsertName = schema.MappingFor(typeof(Target)).As<DocumentMapping>().QualifiedUpsertName;

            schema.SchemaFunctionNames().Contains(upsertName)
                .ShouldBeTrue();

            theCleaner.CompletelyRemove(typeof(Target));

            schema.SchemaFunctionNames().Contains(upsertName)
                .ShouldBeFalse();
        }

        [Fact]
        public void completely_remove_everything()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new User());
            theSession.Store(new Company());
            theSession.Store(new Issue());

            theSession.SaveChanges();
            theSession.Dispose();

            theCleaner.CompletelyRemoveAll();

            var schema = theContainer.GetInstance<DocumentSchema>();

            schema.DocumentTables().Any().ShouldBeFalse();
            schema.SchemaFunctionNames().Any().ShouldBeFalse();
        }

        [Fact]
        public void delete_except_types()
        {
            theSession.Store(new Target { Number = 1 });
            theSession.Store(new Target { Number = 2 });
            theSession.Store(new User());
            theSession.Store(new Company());
            theSession.Store(new Issue());

            theSession.SaveChanges();
            theSession.Dispose();



            theCleaner.DeleteDocumentsExcept(typeof(Target), typeof(User));


            using (var session = theStore.OpenSession())
            {
                // Not cleaned off
                session.Query<Target>().Count().ShouldBe(2);
                session.Query<User>().Count().ShouldBe(1);

                // Should be cleaned off
                session.Query<Issue>().Count().ShouldBe(0);
                session.Query<Company>().Count().ShouldBe(0);
            }
        }
    }
}