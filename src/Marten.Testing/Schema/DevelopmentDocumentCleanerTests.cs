using System.Linq;
using Marten.Schema;
using Marten.Testing.Fixtures;
using Shouldly;
using StructureMap;

namespace Marten.Testing.Schema
{
    public class DevelopmentDocumentCleanerTests
    {
        public void clean_table()
        {
            using (var container = Container.For<DevelopmentModeRegistry>())
            {
                var session = container.GetInstance<IDocumentSession>();

                session.Store(new Target{Number = 1});
                session.Store(new Target{Number = 2});
                session.Store(new Target{Number = 3});
                session.Store(new Target{Number = 4});
                session.Store(new Target{Number = 5});
                session.Store(new Target{Number = 6});

                session.SaveChanges();

                var cleaner = container.GetInstance<DevelopmentDocumentCleaner>();

                cleaner.DocumentsFor(typeof(Target));

                session.Query<Target>().Count().ShouldBe(0);
            }
        }
    }
}