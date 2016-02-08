using System;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using StructureMap;
using Xunit;

namespace Marten.Testing
{
    public class QuerySessionTests
    {
        [Fact]
        public void can_load_a_document_by_id_from_the_database()
        {
            var container = Container.For<DevelopmentModeRegistry>();
            var store = container.GetInstance<IDocumentStore>();

            var guy1 = new FryGuy();
            var guy2 = new FryGuy();
            var guy3 = new FryGuy();

            using (var session = store.OpenSession())
            {
                session.Store(guy1, guy2, guy3);
                session.SaveChanges();
            }

            using (var query = store.QuerySession().As<QuerySession>())
            {
                query.LoadDocument<FryGuy>(guy2.id).ShouldNotBeNull();
            }
        }


        [Fact]
        public async Task can_load_a_document_by_id_from_the_database_async()
        {
            var container = Container.For<DevelopmentModeRegistry>();
            var store = container.GetInstance<IDocumentStore>();

            var guy1 = new FryGuy();
            var guy2 = new FryGuy();
            var guy3 = new FryGuy();

            using (var session = store.OpenSession())
            {
                session.Store(guy1, guy2, guy3);
                session.SaveChanges();
            }

            using (var query = store.QuerySession().As<QuerySession>())
            {
                (await query.LoadDocumentAsync<FryGuy>(guy2.id, new CancellationTokenSource().Token)).ShouldNotBeNull();
            }
        }

        public class FryGuy
        {
            public Guid id;
        }
    }
}