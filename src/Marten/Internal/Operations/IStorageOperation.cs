#nullable enable
using Weasel.Postgresql;

namespace Marten.Internal.Operations;

// #4820: subinterface over Weasel.Core.IStorageOperation (DocumentType / PostprocessAsync /
// Role). It now declares its OWN agnostic ConfigureCommand(ICommandBuilder, IStorageSession)
// instead of inheriting it from Marten's LINQ IQueryHandler — decoupling the storage operations
// from the LINQ query-handler contract so the closed-shape operation runtime is movable to a
// shared package. The signature is identical to the one that used to come from IQueryHandler, so
// the operation implementers are unchanged. (Aligns with the Critter Stack dedupe pillar,
// jasperfx#214 — a Weasel.Core.IStorageOperation.ConfigureCommand member would let this collapse
// into the shared base once it lands upstream.)
public interface IStorageOperation: Weasel.Core.IStorageOperation
{
    void ConfigureCommand(ICommandBuilder builder, Marten.Internal.IStorageSession session);
}
