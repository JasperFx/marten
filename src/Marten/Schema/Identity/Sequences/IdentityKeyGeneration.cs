using System;
using System.Collections.Generic;
using Baseline;
using Marten.Storage;

namespace Marten.Schema.Identity.Sequences
{
    public class IdentityKeyGeneration : IIdGenerationWithDependencies
    {
        private readonly HiloSettings _hiloSettings;
        private readonly DocumentMapping _mapping;

        public IdentityKeyGeneration(DocumentMapping mapping, HiloSettings hiloSettings)
        {
            _mapping = mapping;
            _hiloSettings = hiloSettings;
        }

        public int MaxLo => _hiloSettings.MaxLo;


        public IEnumerable<Type> KeyTypes { get; } = new[] {typeof(string)};

        public IIdGenerator<T> Build<T>(ITenant tenant)
        {
            var sequence = tenant.Sequences.Hilo(_mapping.DocumentType, _hiloSettings);
            return (IIdGenerator<T>) new IdentityKeyGenerator(_mapping.Alias, sequence);
        }

        public Type[] DependentFeatures()
        {
            return new Type[] { typeof(SequenceFactory) };
        }
    }

    public class IdentityKeyGenerator : IIdGenerator<string>
    {
        public IdentityKeyGenerator(string alias, ISequence sequence)
        {
            Alias = alias;
            Sequence = sequence;
        }

        public string Alias { get; set; }
        public ISequence Sequence { get; }

        public string Assign(string existing, out bool assigned)
        {
            if (existing.IsEmpty())
            {
                var next = Sequence.NextLong();
                assigned = true;

                return $"{Alias}/{next}";
            }

            assigned = false;
            return existing;
        }
    }
}