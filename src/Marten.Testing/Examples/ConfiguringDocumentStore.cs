using System;
using System.Linq;
using Marten.Schema;
using Marten.Testing.Documents;

namespace Marten.Testing.Examples
{
    // SAMPLE: user_document
    public class User
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public bool Internal { get; set; }
        public string UserName { get; set; }
    }
    // ENDSAMPLE
    public class ConfiguringDocumentStore
    {
        public void start_a_basic_store()
        {
            // SAMPLE: start_a_store
            var store = DocumentStore
                .For("host=localhost;database=marten_test;password=mypassword;username=someuser");
            // ENDSAMPLE

            // SAMPLE: start_a_query_session
            using (var session = store.QuerySession())
            {
                var internalUsers = session
                    .Query<User>().Where(x => x.Internal).ToArray();
            }
            // ENDSAMPLE

            // SAMPLE: opening_sessions
            // Open a session for querying, loading, and 
            // updating documents 
            using (var session = store.LightweightSession())
            {
                var user = new User {FirstName = "Han", LastName = "Solo"};
                session.Store(user);

                session.SaveChanges();
            }

            // Open a session for querying, loading, and 
            // updating documents with a backing "Identity Map"
            using (var session = store.OpenSession())
            {
                var existing = session
                    .Query<User>()
                    .Where(x => x.FirstName == "Han" && x.LastName == "Solo")
                    .Single();
            }

            // Open a session for querying, loading, and 
            // updating documents that performas automated
            // "dirty" checking of previously loaded documents
            using (var session = store.DirtyTrackedSession())
            {
                
            }
            // ENDSAMPLE
            

        }

        public void start_a_complex_store()
        {
            // SAMPLE: start_a_complex_store
            var store = DocumentStore.For(_ =>
            {
                // Turn this off in production 
                _.AutoCreateSchemaObjects = AutoCreate.None;

                // This is still mandatory
                _.Connection("some connection string");

                // Override the JSON Serialization
                _.Serializer<JilSerializer>();

                // We're getting ahead of ourselves, but this
                // opts into Postgresql 9.5 style upserts
                _.UpsertType = PostgresUpsertType.Standard;
            });
            // ENDSAMPLE
        }

        
    }
}