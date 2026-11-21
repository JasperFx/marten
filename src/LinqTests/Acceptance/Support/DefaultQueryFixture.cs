using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Marten;
using Marten.Services;
using Marten.Testing.Documents;
namespace LinqTests.Acceptance.Support;

public class DefaultQueryFixture: TargetSchemaFixture, IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        Store = await ProvisionStoreAsync("linq_querying");

        DuplicatedFieldStore = await ProvisionStoreAsync("duplicate_fields", o =>
        {
            o.Schema.For<Target>()
                .Duplicate(x => x.Number)
                .Duplicate(x => x.Long)
                .Duplicate(x => x.String)
                .Duplicate(x => x.Date)
                .Duplicate(x => x.Double)
                .Duplicate(x => x.Flag)
                .Duplicate(x => x.Color)
                .Duplicate(x => x.NumberArray);
        });

        FSharpFriendlyStore = await ProvisionStoreAsync("fsharp_linq_querying", options =>
        {
            options.RegisterFSharpOptionValueTypes();
            var serializerOptions = JsonFSharpOptions.Default().WithUnwrapOption().ToJsonSerializerOptions();
            options.UseSystemTextJsonForSerialization(serializerOptions);
        }, isFsharpTest: true);

        FSharpFriendlyStoreWithDuplicatedField = await ProvisionStoreAsync("fsharp_duplicated_fields", options =>
        {
            options.Schema.For<Target>()
                .Duplicate(x => x.Number)
                .Duplicate(x => x.Long)
                .Duplicate(x => x.String)
                .Duplicate(x => x.Date)
                .Duplicate(x => x.Double)
                .Duplicate(x => x.Flag)
                .Duplicate(x => x.Color)
                .Duplicate(x => x.NumberArray);

            options.RegisterFSharpOptionValueTypes();
            var serializerOptions = JsonFSharpOptions.Default().WithUnwrapOption().ToJsonSerializerOptions();
            options.UseSystemTextJsonForSerialization(serializerOptions);
        }, isFsharpTest: true);

        SystemTextJsonStore = await ProvisionStoreAsync("stj_linq", o =>
        {
            o.Serializer<SystemTextJsonSerializer>();
        });
    }

    public async ValueTask DisposeAsync()
    {

    }

    public DocumentStore SystemTextJsonStore { get; set; }

    public DocumentStore DuplicatedFieldStore { get; set; }

    public DocumentStore FSharpFriendlyStore { get; set; }
    public DocumentStore FSharpFriendlyStoreWithDuplicatedField { get; set; }

    public DocumentStore Store { get; set; }

    private readonly object _camelCasingLock = new();
    private Task<DocumentStore> _camelCasingStore;

    /// <summary>
    /// The camel-cased System.Text.Json store, provisioned once on first use. Every row of
    /// child_collection_queries.run_query_with_camel_casing used to drop and rebuild this schema and bulk insert
    /// the documents again, which took 4-5 seconds a row and ran into the 5 second command timeout on a busy box.
    /// The rows only query, so one copy serves them all.
    /// </summary>
    public Task<DocumentStore> CamelCasingStoreAsync()
    {
        lock (_camelCasingLock)
        {
            return _camelCasingStore ??= ProvisionStoreAsync("linq_with_camel_casing",
                opts => opts.UseSystemTextJsonForSerialization(casing: Casing.CamelCase));
        }
    }
}

