#nullable enable
using JasperFx.Events.Projections;

namespace Marten.Events.Projections;

#region sample_IProjection

/// <summary>
///     Interface for all event projections
///     IProjection implementations define the projection type and handle its projection document lifecycle
///     Optimized for inline usage
/// </summary>
public interface IProjection: IJasperFxProjection<IDocumentOperations>
{
}

#endregion
