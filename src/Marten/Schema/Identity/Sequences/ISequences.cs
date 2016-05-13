using System;

namespace Marten.Schema.Identity.Sequences
{
    public interface ISequences
    {
        ISequence Hilo(Type documentType, HiloSettings settings);
    }
}