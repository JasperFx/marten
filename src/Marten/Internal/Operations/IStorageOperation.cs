#nullable enable
using Marten.Linq.QueryHandlers;

namespace Marten.Internal.Operations;

// Subinterface over Weasel.Core.IStorageOperation. The three storage-op members
// (DocumentType, PostprocessAsync, Role) come from Weasel.Core; ConfigureCommand
// with the session-aware signature comes from IQueryHandler. Polecat layers its
// own ICommandBuilder-only ConfigureCommand on the same Weasel.Core base — see
// #4500 / Critter Stack 2026 dedupe pillar (jasperfx#214).
public interface IStorageOperation: Weasel.Core.IStorageOperation, IQueryHandler
{
}
