using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;

namespace LinqTests.Acceptance.Support;

public abstract class TargetSchemaFixture: IDisposable
{
    public readonly Target[] Documents = Target.GenerateRandomData(1000).ToArray();

    private readonly IList<DocumentStore> _stores = new List<DocumentStore>();

    public void Dispose()
    {
        foreach (var documentStore in _stores)
        {
            documentStore.Dispose();
        }
    }

    internal DocumentStore ProvisionStore(string schema, Action<StoreOptions> configure = null)
    {

        var store = DocumentStore.For(x =>
        {
            x.Connection(ConnectionSource.ConnectionString);
            x.DatabaseSchemaName = schema;
            x.RegisterFSharpOptionValueTypes();
            var serializerOptions = JsonFSharpOptions.Default().WithUnwrapOption().ToJsonSerializerOptions();
            x.UseSystemTextJsonForSerialization(serializerOptions);

            configure?.Invoke(x);
        });

        store.Advanced.Clean.CompletelyRemoveAll();

        store.BulkInsert(Documents);

        _stores.Add(store);

        return store;
    }
}
