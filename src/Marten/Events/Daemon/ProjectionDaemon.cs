using JasperFx.Events.Daemon;
using JasperFx.Events.Daemon.HighWater;
using Marten.Storage;
using Microsoft.Extensions.Logging;

namespace Marten.Events.Daemon;

public partial class ProjectionDaemon: JasperFxAsyncDaemon<IDocumentOperations, IQuerySession>, IProjectionDaemon
{
    public ProjectionDaemon(DocumentStore store, MartenDatabase database, ILoggerFactory loggerFactory,
        IHighWaterDetector detector, DaemonSettings settings)
        : base(store, database, loggerFactory, detector, settings)
    {
    }

    public ProjectionDaemon(DocumentStore store, MartenDatabase database, ILogger logger,
        IHighWaterDetector detector, DaemonSettings settings)
        : base(store, database, logger, detector, settings)
    {
    }
}
