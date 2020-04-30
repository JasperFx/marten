using System;
using Marten.Testing.Documents;

namespace Marten.Testing.Examples
{
    public class Load_by_Id
    {
        // SAMPLE: load_by_id
        public void load_by_id(IDocumentSession session)
        {
            var userId = Guid.NewGuid();

            // Load a single document identified by a Guid
            var user = session.Load<User>(userId);

            // There's an overload of Load for integers and longs
            var doc = session.Load<IntDoc>(15);

            // Another overload for documents identified by strings
            var doc2 = session.Load<StringDoc>("Hank");

            // Load multiple documents by a group of id's
            var users = session.LoadMany<User>(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

            var ids = new Guid[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

            // If you already have an array of id values
            var users2 = session.LoadMany<User>(ids);
        }

        // ENDSAMPLE
    }
}
