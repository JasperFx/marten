using System.Linq;

namespace Marten.Testing.Examples
{
    public class OnePointOneNotes
    {
        public void use_distinct(IQuerySession session)
        {
            var surnames = session
                .Query<User>()
                .Select(x => x.LastName)
                .Distinct();
        }

        public void lazy_tx(IDocumentSession session)
        {
            // Executing this query will *not* start
            // a new transaction
            var users = session
                .Query<User>()
                .Where(x => x.Internal)
                .ToList();

            session.Store(new User { UserName = "lebron" });

            // This starts a transaction against the open
            // connection before doing any writes
            session.SaveChanges();
        }

        public void reset_hilo(IDocumentStore store)
        {
            // This resets the Hilo state in the database so that
            // all id's assigned will be greater than the floor
            // value.
            store.Tenancy.Default.ResetHiloSequenceFloor<IntDoc>(3000);
        }

        public void bulk_inserts(IDocumentStore store, Target[] documents)
        {
            store.BulkInsert(documents, BulkInsertMode.IgnoreDuplicates);

            // or

            store.BulkInsert(documents, BulkInsertMode.OverwriteExisting);
        }
    }
}
