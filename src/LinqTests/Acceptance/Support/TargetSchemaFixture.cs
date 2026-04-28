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
    public readonly Target[] Documents;
    public readonly Target[] FSharpDocuments;

    private readonly IList<DocumentStore> _stores = new List<DocumentStore>();

    protected TargetSchemaFixture()
    {
        // Reset the shared static Random in Target before generating the
        // fixture data so the fixture's documents are deterministic regardless
        // of which other tests happened to consume Target.GenerateRandomData
        // earlier in this test process. Without this, occasional CI runs see
        // a dataset where (e.g.) no Target satisfies the
        // StringArray/NumberArray/Inner predicate that select_clauses relies
        // on, producing an NRE in SelectTransform, or where two Targets share
        // a Long value and Postgres' unstable ORDER BY tiebreak diverges from
        // LINQ-to-Objects in take_and_skip. See #4310.
        Target.ResetRandomSeed();
        Documents = Target.GenerateRandomData(1000).ToArray();
        FSharpDocuments = Target.GenerateRandomData(1000, includeFSharpUnionTypes: true).ToArray();
    }

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
