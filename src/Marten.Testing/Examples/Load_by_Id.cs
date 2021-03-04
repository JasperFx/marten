using System;
using System.Threading;
using System.Threading.Tasks;
using Marten.Testing.Documents;

namespace Marten.Testing.Examples
{
    public class Load_by_Id
    {
        // SAMPLE: load_by_id
        public void LoadById(IDocumentSession session)
        {
            var userId = Guid.NewGuid();

            // Load a single document identified by a Guid
            var user = session.Load<User>(userId);

            // There's an overload of Load for integers and longs
            var doc = session.Load<IntDoc>(15);

            // Another overload for documents identified by strings
            var doc2 = session.Load<StringDoc>("Hank");

            // Load multiple documents by a group of ids
            var users = session.LoadMany<User>(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

            var ids = new Guid[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

            // If you already have an array of id values
            var users2 = session.LoadMany<User>(ids);
        }
        // ENDSAMPLE

        // SAMPLE: async_load_by_id
        public async Task LoadByIdAsync(IQuerySession session, CancellationToken token = default (CancellationToken))
        {
            var userId = Guid.NewGuid();

            // Load a single document identified by a Guid
            var user = await session.LoadAsync<User>(userId, token);

            // There's an overload of Load for integers and longs
            var doc = await session.LoadAsync<IntDoc>(15, token);

            // Another overload for documents identified by strings
            var doc2 = await session.LoadAsync<StringDoc>("Hank", token);

            // Load multiple documents by a group of ids
            var users = await session.LoadManyAsync<User>(token, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

            var ids = new Guid[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

            // If you already have an array of id values
            var users2 = await session.LoadManyAsync<User>(token, ids);
        }
        // ENDSAMPLE
    }
}
