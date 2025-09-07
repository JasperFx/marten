using JasperFx.Events.Daemon;
using JasperFx.Events.Daemon.HighWater;
using Marten.Events.Projections;
using Marten.Storage;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon;

public partial class ProjectionDaemon: JasperFxAsyncDaemon<IDocumentOperations, IQuerySession, IProjection>, IProjectionDaemon
{
    public ProjectionDaemon(DocumentStore store, MartenDatabase database, ILoggerFactory loggerFactory,
        IHighWaterDetector detector)
        : base(store, database, loggerFactory, detector, store.Options.Projections)
    {

    }

    public ProjectionDaemon(DocumentStore store, MartenDatabase database, ILogger logger,
        IHighWaterDetector detector)
        : base(store, database, logger, detector, store.Options.Projections)
    {
    }
}
