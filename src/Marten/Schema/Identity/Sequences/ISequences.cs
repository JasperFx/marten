using System;
using Marten.Storage;
using Weasel.Postgresql;

#nullable enable
namespace Marten.Schema.Identity.Sequences
{
    public interface ISequences: IFeatureSchema
    {
        ISequence Hilo(Type documentType, HiloSettings settings);

        ISequence SequenceFor(Type documentType);
    }
}
