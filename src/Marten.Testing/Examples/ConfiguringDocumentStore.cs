using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using Marten.Services;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Newtonsoft.Json;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Testing.Examples;
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
    public string? UserName { get; set; }
    public string? Department { get; set; }
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

        await using (var session = store.QuerySession())
        {
            var internalUsers = await session
                .Query<User>()
                .Where(x => x.Internal)
                .ToListAsync();
        }
        #endregion

        #region sample_opening_sessions
        // Open a session for querying, loading, and
        // updating documents
        await using (var session = store.LightweightSession())
        {
            var user = new User { FirstName = "Han", LastName = "Solo" };
            session.Store(user);

            await session.SaveChangesAsync();
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
        });
        #endregion
    }

    public void customize_serializer()
    {
        #region sample_customize_serializer
        var store = DocumentStore.For(_ =>
        {
            _.Connection("some connection string");

            // Newtonsoft - Enabled by default
            _.UseNewtonsoftForSerialization(); // [!code ++]

            // System.Text.Json - Opt in
            _.UseSystemTextJsonForSerialization(); // [!code ++]
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
        serializer.Configure(_ =>
        {
            // Code directly against a Newtonsoft.Json JsonSerializer
            _.DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind;
            _.ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor;
        });

        var store = DocumentStore.For(_ =>
        {
            // Replace the default JsonNetSerializer with the one we configured
            // above
            _.Serializer(serializer);
        });
        #endregion
    }

    public void customize_json_enum_storage_serialization()
    {
        #region sample_customize_json_enum_storage_serialization

        var store = DocumentStore.For(_ =>
        {
            // Newtonsoft // [!code focus:5]
            _.UseNewtonsoftForSerialization(enumStorage: EnumStorage.AsString);

            // STJ
            _.UseSystemTextJsonForSerialization(enumStorage: EnumStorage.AsString);
        });
        #endregion
    }

    public void customize_json_camelcase_casing_serialization()
    {
        #region sample_customize_json_camelcase_casing_serialization

        var store = DocumentStore.For(_ =>
        {
            // Newtonsoft // [!code focus:5]
            _.UseNewtonsoftForSerialization(casing: Casing.CamelCase);

            // STJ
            _.UseSystemTextJsonForSerialization(casing: Casing.CamelCase);
        });
        #endregion
    }

    public void customize_json_net_snakecase_collectionstorage()
    {
        #region sample_customize_json_net_snakecase_collectionstorage

        var store = DocumentStore.For(_ =>
        {
            // Replace the default (strongly typed) JsonNetSerializer collection storage // [!code focus:3]
            // with JSON array formatting
            _.UseNewtonsoftForSerialization(collectionStorage: CollectionStorage.AsArray);
        });
        #endregion
    }

    public void customize_json_net_nonpublicsetters()
    {
        #region sample_customize_json_net_nonpublicsetters

        var store = DocumentStore.For(_ =>
        {
             // Allow the JsonNetSerializer to also deserialize using non-public setters // [!code focus:2]
            _.UseNewtonsoftForSerialization(nonPublicMembersStorage: NonPublicMembersStorage.NonPublicSetters);
        });
        #endregion
    }

    public void customize_serializers_advanced()
    {
        #region sample_customize_json_advanced
        var store = DocumentStore.For(_ =>
        {
            _.UseNewtonsoftForSerialization( // [!code focus:14]
                enumStorage: EnumStorage.AsString,
                configure: settings =>
                {
                    settings.DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind;
                    settings.ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor;
                });

            _.UseSystemTextJsonForSerialization(
                enumStorage: EnumStorage.AsString,
                configure: settings =>
                {
                    settings.MaxDepth = 100;
                });
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
