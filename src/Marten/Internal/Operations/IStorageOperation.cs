#nullable enable
using Marten.Linq.QueryHandlers;

namespace Marten.Internal.Operations;

/// <summary>
/// Marten's storage-operation contract. Extends the canonical
/// <see cref="Weasel.Core.IStorageOperation"/> (DocumentType, Role, PostprocessAsync)
/// with Marten's <see cref="IQueryHandler"/> for command-building integration.
/// <para>
/// The previous synchronous <c>void Postprocess(...)</c> overload was removed in
/// Marten 9 per the dedup audit (#4351 / pillar JasperFx/jasperfx#214). Callers
/// that still run synchronously bridge to <see cref="Weasel.Core.IStorageOperation.PostprocessAsync"/>
/// via <c>.GetAwaiter().GetResult()</c> until Marten's sync-IO session paths are
/// retired in a follow-up.
/// </para>
/// </summary>
public interface IStorageOperation: Weasel.Core.IStorageOperation, IQueryHandler
{
}
