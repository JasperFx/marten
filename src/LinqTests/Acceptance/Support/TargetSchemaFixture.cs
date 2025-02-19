using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;

namespace LinqTests.Acceptance.Support;

public abstract class TargetSchemaFixture: IDisposable
{
    /*
     * Newtonsoft.Json does not support saving Discriminated Unions unwrapped (included f# options) which causes serialization-related errors.
     * We must therefore only include F# data in F#-related tests to avoid false negatives.
     */
    public readonly Target[] Documents = Target.GenerateRandomData(1000).ToArray();
    public readonly Target[] FSharpDocuments = Target.GenerateRandomData(1000, includeFSharpUnionTypes: true).ToArray();

    private readonly IList<DocumentStore> _stores = new List<DocumentStore>();

    public void Dispose()
    {
        foreach (var documentStore in _stores)
        {
            documentStore.Dispose();
        }
    }

    internal async Task<DocumentStore> ProvisionStoreAsync(string schema, Action<StoreOptions> configure = null, bool isFsharpTest = false)
    {
        var store = DocumentStore.For(x =>
        {
            x.Connection(ConnectionSource.ConnectionString);
            x.DatabaseSchemaName = schema;

            configure?.Invoke(x);
        });

        await store.Advanced.Clean.CompletelyRemoveAllAsync();


        if (isFsharpTest)
        {
            await store.BulkInsertAsync(FSharpDocuments);
        }
        else
        {
            await store.BulkInsertAsync(Documents);
        }

        _stores.Add(store);

        return store;
    }
}
