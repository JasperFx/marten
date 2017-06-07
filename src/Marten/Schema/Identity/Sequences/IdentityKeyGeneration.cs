using System;
using System.Collections.Generic;
using Baseline;
using Marten.Storage;

namespace Marten.Schema.Identity.Sequences
{
    public class IdentityKeyGeneration : IIdGeneration
    {
        private readonly HiloSettings _hiloSettings;
        private readonly DocumentMapping _mapping;

        public IdentityKeyGeneration(DocumentMapping mapping, HiloSettings hiloSettings)
        {
            _mapping = mapping;
            _hiloSettings = hiloSettings ?? new HiloSettings();
        }

        public int MaxLo => _hiloSettings.MaxLo;


        public IEnumerable<Type> KeyTypes { get; } = new[] {typeof(string)};

        public IIdGenerator<T> Build<T>()
        {
            return (IIdGenerator<T>) new IdentityKeyGenerator(_mapping.DocumentType, _mapping.Alias);
        }

        public bool RequiresSequences { get; } = true;

        public Type[] DependentFeatures()
        {
            return new Type[] { typeof(SequenceFactory) };
        }
    }

    public class IdentityKeyGenerator : IIdGenerator<string>
    {
        private readonly Type _documentType;

        public IdentityKeyGenerator(Type documentType, string alias)
        {
            _documentType = documentType;
            Alias = alias;
        }

        public string Alias { get; set; }

        public string Assign(ITenant tenant, string existing, out bool assigned)
        {
            if (existing.IsEmpty())
            {
                var next = tenant.Sequences.SequenceFor(_documentType).NextLong();
                assigned = true;

                return $"{Alias}/{next}";
            }

            assigned = false;
            return existing;
        }
    }
}