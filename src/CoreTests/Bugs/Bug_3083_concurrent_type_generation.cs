using System.Collections.Generic;
using System.Threading.Tasks;
using Marten;
using Marten.Internal;
using Marten.Internal.Storage;
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

    public class SomeDocument
    {
        public string Id { get; set; } = string.Empty;
    }
}
