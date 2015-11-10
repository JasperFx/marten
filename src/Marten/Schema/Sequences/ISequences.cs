using System;

namespace Marten.Schema.Sequences
{
    public interface ISequences
    {
        ISequence HiLo(Type documentType, HiloDef def);
    }
}