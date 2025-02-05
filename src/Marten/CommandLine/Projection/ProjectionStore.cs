using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core.Reflection;
using Marten.Events.Daemon;
using Marten.Events.Projections;
using Marten.Storage;

namespace Marten.CommandLine.Projection;

internal class ProjectionStore: IProjectionStore
{
    public ProjectionStore(DocumentStore store)
    {
        InnerStore = store;

        if (InnerStore.GetType() == typeof(DocumentStore))
        {
            Name = nameof(IDocumentStore);
        }
        else
        {
            var @interface = store.GetType().GetInterfaces().FirstOrDefault(x => x != typeof(IDocumentStore) && x.CanBeCastTo<IDocumentStore>());
            Name = @interface?.Name ?? store.GetType().NameInCode();
        }

        Shards = store
            .Options
            .Projections
            .All
            .Where(x => x.Lifecycle != ProjectionLifecycle.Live)
            .SelectMany(x => x.AsyncProjectionShards(store)).ToList();

    }

    public string Name { get; }
    public IReadOnlyList<AsyncProjectionShard> Shards { get; }
    public async ValueTask<IReadOnlyList<IProjectionDatabase>> BuildDatabases()
    {
        var databases = await InnerStore.Storage.AllDatabases().ConfigureAwait(false);
        return databases.OfType<MartenDatabase>().OrderBy(x => x.Identifier).Select(db => new ProjectionDatabase(this, db)).ToList();
    }

    public DocumentStore InnerStore { get; }
}