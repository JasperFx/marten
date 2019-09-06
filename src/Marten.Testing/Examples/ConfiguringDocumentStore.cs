using System;
using System.Linq;
using Marten.Services;
using Newtonsoft.Json;

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
        public string Department { get; set; }
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
                var user = new User { FirstName = "Han", LastName = "Solo" };
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
                _.Serializer<TestsSerializer>();
            });
            // ENDSAMPLE
        }

        public void customize_json_net_serialization()
        {
            // SAMPLE: customize_json_net_serialization
            var serializer = new Marten.Services.JsonNetSerializer();

            // To change the enum storage policy to store Enum's as strings:
            serializer.EnumStorage = EnumStorage.AsString;

            // All other customizations:
            serializer.Customize(_ =>
            {
                // Code directly against a Newtonsoft.Json JsonSerializer
                _.DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind;
                _.ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor;
            });

            var store = DocumentStore.For(_ =>
            {
                _.Connection("some connection string");

                // Replace the default JsonNetSerializer with the one we configured
                // above
                _.Serializer(serializer);
            });
            // ENDSAMPLE
        }

        public void setting_event_schema()
        {
            // SAMPLE: setting_event_schema
            var store = DocumentStore.For(_ =>
            {
                _.Connection("some connection string");

                // Places all the Event Store schema objects
                // into the "events" schema
                _.Events.DatabaseSchemaName = "events";
            });
            // ENDSAMPLE
        }

        // SAMPLE: custom-store-options
        public class MyStoreOptions: StoreOptions
        {
            public static IDocumentStore ToStore()
            {
                return new DocumentStore(new MyStoreOptions());
            }

            public MyStoreOptions()
            {
                Connection(ConnectionSource.ConnectionString);

                Serializer(new JsonNetSerializer { EnumStorage = EnumStorage.AsString });

                Schema.For<User>().Index(x => x.UserName);
            }
        }

        // ENDSAMPLE
    }
}
