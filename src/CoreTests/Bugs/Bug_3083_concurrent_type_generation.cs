using System.Collections.Generic;
using System.Threading.Tasks;
using Marten;
using Marten.Internal;
using Marten.Internal.Storage;
using Marten.Schema.BulkLoading;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace CoreTests.Bugs;

public class Bug_3083_concurrent_type_generation: BugIntegrationContext
{
    [Fact]
    public async Task concurrent_type_generation()
    {
        var graph = new ProviderGraph(new StoreOptions());

        var tasks = new List<Task<DocumentProvider<SomeDocument>>>();

        for (var i = 0; i < 15; ++i)
        {
            var task = Task.Run(() => graph.StorageFor<SomeDocument>());

            tasks.Add(task);
        }

        var storages = new HashSet<DocumentProvider<SomeDocument>>(ReferenceEqualityComparer.Instance);

        foreach (var task in tasks)
        {
            var storage = await task;
            storages.Add(storage);
        }

        storages.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task concurrent_append_providers()
    {
        var graph = new ProviderGraph(new StoreOptions());

        var tasks = new List<Task>();

        var documentProvider1 = new MockDocumentProvider<SomeDocument>();
        var documentProvider2 = new MockDocumentProvider<OtherDocument>();
        var documentProvider3 = new MockDocumentProvider<ThirdDocument>();
        var documentProvider4 = new MockDocumentProvider<ForthDocument>();

        tasks.Add(Task.Run(() => graph.Append(documentProvider1)));
        tasks.Add(Task.Run(() => graph.Append(documentProvider2)));
        tasks.Add(Task.Run(() => graph.Append(documentProvider3)));
        tasks.Add(Task.Run(() => graph.Append(documentProvider4)));

        await Task.WhenAll(tasks);

        graph.StorageFor<SomeDocument>().ShouldBeSameAs(documentProvider1);
        graph.StorageFor<OtherDocument>().ShouldBeSameAs(documentProvider2);
        graph.StorageFor<ThirdDocument>().ShouldBeSameAs(documentProvider3);
        graph.StorageFor<ForthDocument>().ShouldBeSameAs(documentProvider4);
    }

    public class MockDocumentProvider<T>: DocumentProvider<T> where T : notnull
    {
        public MockDocumentProvider(): this(null, null, null, null, null)
        {
        }

        public MockDocumentProvider(IBulkLoader<T> bulkLoader, IDocumentStorage<T> queryOnly,
            IDocumentStorage<T> lightweight, IDocumentStorage<T> identityMap, IDocumentStorage<T> dirtyTracking): base(
            bulkLoader, queryOnly, lightweight, identityMap, dirtyTracking)
        {
        }
    }

    public class SomeDocument
    {
        public string Id { get; set; } = string.Empty;
    }

    public class OtherDocument
    {
        public string Id { get; set; } = string.Empty;
    }

    public class ThirdDocument
    {
        public string Id { get; set; } = string.Empty;
    }

    public class ForthDocument
    {
        public string Id { get; set; } = string.Empty;
    }
}
