#nullable enable
using Weasel.Postgresql;

namespace Marten.Internal.Operations;

// #4820/#4821: Marten's storage operation derives from the dialect-neutral
// Weasel.Storage.IStorageOperation (itself over Weasel.Core.IStorageOperation — DocumentType /
// PostprocessAsync / Role). Marten's operation implementers keep authoring the Postgres-typed
// ConfigureCommand below; the default interface method bridges the neutral
// ConfigureCommand(Weasel.Core.ICommandBuilder, IStorageSession) slot down to it, mirroring how
// the Weasel.Postgresql ISqlFragment bridges the neutral Apply.
public interface IStorageOperation: Weasel.Storage.IStorageOperation
{
    void ConfigureCommand(ICommandBuilder builder, IStorageSession session);

    void Weasel.Storage.IStorageOperation.ConfigureCommand(Weasel.Core.ICommandBuilder builder,
        IStorageSession session)
    {
        ConfigureCommand((ICommandBuilder)builder, session);
    }
}
