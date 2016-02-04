using System;

namespace Marten.Schema.Sequences
{
    public interface ISequences
    {
        ISequence Hilo(Type documentType, HiloSettings settings);
    }
}