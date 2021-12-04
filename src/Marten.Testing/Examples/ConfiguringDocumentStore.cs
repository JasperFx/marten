using System.Linq;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Newtonsoft.Json;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Testing.Examples
{
    // Leave this commented out please, and always use the User
    // in Marten.Testing.Documents
    /*

    #region sample_user_document
    public class User
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public bool Internal { get; set; }
        public string UserName { get; set; }
        public string Department { get; set; }
    }

    #endregion
    */


    public class ConfiguringDocumentStore
    {
        public async Task start_a_basic_store()
        {
            #region sample_start_a_store
            var store = DocumentStore
                .For("host=localhost;database=marten_testing;password=mypassword;username=someuser");
            #endregion

            #region sample_start_a_query_session
            using (var session = store.QuerySession())
            {
                var internalUsers = session
                    .Query<User>().Where(x => x.Internal).ToArray();
            }
            #endregion

            #region sample_opening_sessions
            // Open a session for querying, loading, and
            // updating documents
            using (var session = store.LightweightSession())
            {
                var user = new User { FirstName = "Han", LastName = "Solo" };
                session.Store(user);

                await session.SaveChangesAsync();
            }

            // Open a session for querying, loading, and
            // updating documents with a backing "Identity Map"
            using (var session = store.QuerySession())
            {
                var existing = await session
                    .Query<User>()
                    .SingleAsync(x => x.FirstName == "Han" && x.LastName == "Solo");
            }
            #endregion
        }

        public void start_a_complex_store()
        {
            #region sample_start_a_complex_store
            var store = DocumentStore.For(_ =>
            {
                // Turn this off in production
                _.AutoCreateSchemaObjects = AutoCreate.None;

                // This is still mandatory
                _.Connection("some connection string");

                // Override the JSON Serialization
                _.Serializer<TestsSerializer>();
            });
            #endregion
        }

        public void customize_json_net_serialization()
        {
            #region sample_customize_json_net_serialization
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
            #endregion
        }

        public void customize_json_net_enum_storage_serialization()
        {
            #region sample_customize_json_net_enum_storage_serialization

            var store = DocumentStore.For(_ =>
            {
                _.Connection("some connection string");

                // Replace the default JsonNetSerializer default enum storage
                // with storing them as string
                _.UseDefaultSerialization(enumStorage: EnumStorage.AsString);
            });
            #endregion
        }

        public void customize_json_net_camelcase_casing_serialization()
        {
            #region sample_customize_json_net_camelcase_casing_serialization

            var store = DocumentStore.For(_ =>
            {
                _.Connection("some connection string");

                // Replace the default (as is) JsonNetSerializer field names casing
                // with camelCase formatting
                _.UseDefaultSerialization(casing: Casing.CamelCase);
            });
            #endregion
        }

        public void customize_json_net_snakecase_casing_serialization()
        {
            #region sample_customize_json_net_snakecase_casing_serialization

            var store = DocumentStore.For(_ =>
            {
                _.Connection("some connection string");

                // Replace the default (as is) JsonNetSerializer field names casing
                // with snake_case formatting
                _.UseDefaultSerialization(casing: Casing.SnakeCase);
            });
            #endregion
        }

        public void customize_json_net_snakecase_collectionstorage()
        {
            #region sample_customize_json_net_snakecase_collectionstorage

            var store = DocumentStore.For(_ =>
            {
                _.Connection("some connection string");

                // Replace the default (strongly typed) JsonNetSerializer collection storage
                // with JSON array formatting
                _.UseDefaultSerialization(collectionStorage: CollectionStorage.AsArray);
            });
            #endregion
        }

        public void customize_json_net_nonpublicsetters()
        {
            #region sample_customize_json_net_nonpublicsetters

            var store = DocumentStore.For(_ =>
            {
                _.Connection("some connection string");

                // Replace the default (only public setters) JsonNetSerializer deserialization settings
                // with allowing to also deserialize using non-public setters
                _.UseDefaultSerialization(nonPublicMembersStorage: NonPublicMembersStorage.NonPublicSetters);
            });
            #endregion
        }

        public void setting_event_schema()
        {
            #region sample_setting_event_schema
            var store = DocumentStore.For(_ =>
            {
                _.Connection("some connection string");

                // Places all the Event Store schema objects
                // into the "events" schema
                _.Events.DatabaseSchemaName = "events";
            });
            #endregion
        }

        #region sample_custom-store-options
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

        #endregion


        public void set_multi_tenancy_on_events()
        {
            #region sample_making_the_events_multi_tenanted

            var store = DocumentStore.For(opts =>
            {
                opts.Connection("some connection string");

                // And that's all it takes, the events are now multi-tenanted
                opts.Events.TenancyStyle = TenancyStyle.Conjoined;
            });

            #endregion
        }
    }
}
