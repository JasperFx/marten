#nullable enable
using System;
using Weasel.Core.Migrations;

namespace Marten.Schema.Identity.Sequences;

public interface ISequences: IFeatureSchema
{
    ISequence Hilo(Type documentType, HiloSettings settings);

    ISequence SequenceFor(Type documentType);
}
