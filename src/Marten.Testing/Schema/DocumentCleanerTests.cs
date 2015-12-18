using System.Linq;
using Marten.Generation;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Fixtures;
using Shouldly;
using StructureMap;
using Xunit;

namespace Marten.Testing.Schema
{
    public class DocumentCleanerTests
    {
        [Fact]
        public void clean_table()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                var session = container.GetInstance<IDocumentStore>().OpenSession();

                session.Store(new Target{Number = 1});
                session.Store(new Target{Number = 2});
                session.Store(new Target{Number = 3});
                session.Store(new Target{Number = 4});
                session.Store(new Target{Number = 5});
                session.Store(new Target{Number = 6});

                session.SaveChanges();

                var cleaner = container.GetInstance<DocumentCleaner>();

                cleaner.DeleteDocumentsFor(typeof(Target));

                session.Query<Target>().Count().ShouldBe(0);
            }
        }

        [Fact]
        public void delete_all_documents()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                var session = container.GetInstance<IDocumentStore>().OpenSession();

                session.Store(new Target { Number = 1 });
                session.Store(new Target { Number = 2 });
                session.Store(new User());
                session.Store(new Company());
                session.Store(new Issue());

                session.SaveChanges();

                var cleaner = container.GetInstance<DocumentCleaner>();

                cleaner.DeleteAllDocuments();

                session.Query<Target>().Count().ShouldBe(0);
                session.Query<User>().Count().ShouldBe(0);
                session.Query<Issue>().Count().ShouldBe(0);
                session.Query<Company>().Count().ShouldBe(0);
            }
        }

        [Fact]
        public void completely_remove_document_type()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                var session = container.GetInstance<IDocumentStore>().OpenSession();

                session.Store(new Target { Number = 1 });
                session.Store(new Target { Number = 2 });

                session.SaveChanges();

                var schema = container.GetInstance<Marten.Schema.DocumentSchema>();
                schema.DocumentTables().Contains(DocumentMapping.TableNameFor(typeof(Target)))
                    .ShouldBeTrue();

                var cleaner = container.GetInstance<DocumentCleaner>();

                cleaner.CompletelyRemove(typeof(Target));

                schema.DocumentTables().Contains(DocumentMapping.TableNameFor(typeof(Target)))
                    .ShouldBeFalse();

            }
        }

        [Fact]
        public void completely_remove_document_removes_the_upsert_command_too()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                var session = container.GetInstance<IDocumentStore>().OpenSession();

                session.Store(new Target { Number = 1 });
                session.Store(new Target { Number = 2 });

                session.SaveChanges();

                var schema = container.GetInstance<DocumentSchema>();
                
                schema.SchemaFunctionNames().Contains(DocumentMapping.UpsertNameFor(typeof(Target)))
                    .ShouldBeTrue();

                var cleaner = container.GetInstance<DocumentCleaner>();

                cleaner.CompletelyRemove(typeof(Target));

                schema.SchemaFunctionNames().Contains(DocumentMapping.UpsertNameFor(typeof(Target)))
                    .ShouldBeFalse();

            }
        }

        [Fact]
        public void completely_remove_everything()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                var session = container.GetInstance<IDocumentStore>().OpenSession();

                session.Store(new Target { Number = 1 });
                session.Store(new Target { Number = 2 });
                session.Store(new User());
                session.Store(new Company());
                session.Store(new Issue());

                session.SaveChanges();

                var cleaner = container.GetInstance<DocumentCleaner>();

                cleaner.CompletelyRemoveAll();

                var runner = container.GetInstance<DocumentSchema>();

                runner.DocumentTables().Any().ShouldBeFalse();
                runner.SchemaFunctionNames().Any().ShouldBeFalse();
            }
        }

        [Fact]
        public void delete_except_types()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                var session = container.GetInstance<IDocumentStore>().OpenSession();

                session.Store(new Target { Number = 1 });
                session.Store(new Target { Number = 2 });
                session.Store(new User());
                session.Store(new Company());
                session.Store(new Issue());

                session.SaveChanges();

                var cleaner = container.GetInstance<DocumentCleaner>();

                cleaner.DeleteDocumentsExcept(typeof(Target), typeof(User));

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