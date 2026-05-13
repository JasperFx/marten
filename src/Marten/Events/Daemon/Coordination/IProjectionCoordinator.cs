using JasperFx.Events.Daemon;

namespace Marten.Events.Daemon.Coordination;

/// <summary>
/// Marten's projection coordinator marker. The canonical contract lives in
/// <see cref="JasperFx.Events.Daemon.IProjectionCoordinator"/>; this empty inheriting
/// interface preserves source compatibility for the
/// <c>Marten.Events.Daemon.Coordination</c> namespace.
/// </summary>
public interface IProjectionCoordinator : JasperFx.Events.Daemon.IProjectionCoordinator
{
}

/// <summary>
/// Marten-typed projection coordinator marker. Tightens the canonical
/// <c>where T : class</c> constraint to <c>where T : IDocumentStore</c> so ancillary
/// Marten stores can be registered as distinct services.
/// </summary>
public interface IProjectionCoordinator<T> : IProjectionCoordinator, JasperFx.Events.Daemon.IProjectionCoordinator<T>
    where T : class, IDocumentStore
{
}
