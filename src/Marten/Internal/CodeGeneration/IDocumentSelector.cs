namespace Marten.Internal.CodeGeneration;

/// <summary>
/// Marker interface implemented by document selectors whose state is
/// captured per-<see cref="IMartenSession"/> (identity map, version /
/// revision trackers, dirty-check tracker registry). The compiled-query
/// pipeline uses it via <c>IMaybeStatefulHandler.DependsOnDocumentSelector</c>
/// to decide whether to clone the handler per session vs. share a
/// single instance across sessions.
/// </summary>
/// <remarks>
/// Pre-#4404 this lived alongside the Roslyn-emitted document selectors.
/// With the closed-shape hierarchy it's implemented by
/// <c>ClosedShapeLightweightSelector</c>, <c>ClosedShapeIdentityMapSelector</c>,
/// and <c>ClosedShapeDirtyTrackingSelector</c>;
/// <c>ClosedShapeQueryOnlySelector</c> is stateless and does not.
/// </remarks>
public interface IDocumentSelector
{
}
