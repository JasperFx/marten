#nullable enable
using System;
using Weasel.Core.Migrations;
using Weasel.Core.Sequences;

namespace Marten.Schema.Identity.Sequences;

// #4811: ISequenceSource (Weasel.Core) is the database-agnostic sequence seam the shared
// Weasel.Core.Identity strategies resolve Hi-Lo sequences through. ISequences already declares
// the matching SequenceFor(Type), so implementing the base is a no-op for existing implementers.
public interface ISequences: IFeatureSchema, ISequenceSource
{
    ISequence Hilo(Type documentType, HiloSettings settings);

    ISequence SequenceFor(Type documentType);
}
