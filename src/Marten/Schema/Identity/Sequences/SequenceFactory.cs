using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Weasel.Postgresql;
using Marten.Storage;
using Weasel.Core;
using Weasel.Postgresql.Tables;

#nullable enable
namespace Marten.Schema.Identity.Sequences
{
    public class SequenceFactory: ISequences
    {
        private readonly StoreOptions _options;
        private readonly ITenant _tenant;
        private readonly ConcurrentDictionary<string, ISequence> _sequences = new ConcurrentDictionary<string, ISequence>();

        public SequenceFactory(StoreOptions options, ITenant tenant)
        {
            _options = options;
            _tenant = tenant;
        }

        public string Name { get; } = "mt_hilo";

        public IEnumerable<Type> DependentTypes()
        {
            yield break;
        }

        public bool IsActive(StoreOptions options) => true;

        public ISchemaObject[] Objects
        {
            get
            {
                var table = new Table(new DbObjectName(_options.DatabaseSchemaName, "mt_hilo"));
                table.AddColumn<string>("entity_name").AsPrimaryKey();
                table.AddColumn<long>("hi_value").DefaultValue(0L);

                var function = new SystemFunction(_options, "mt_get_next_hi", "varchar");

                return new ISchemaObject[]
                {
                    table,
                    function
                };
            }
        }

        public Type StorageType { get; } = typeof(SequenceFactory);
        public string Identifier { get; } = "hilo";

        public void WritePermissions(DdlRules rules, TextWriter writer)
        {
            // Nothing
        }

        public ISequence SequenceFor(Type documentType)
        {
            return Hilo(documentType, _options.Storage.MappingFor(documentType).HiloSettings ?? _options.Advanced.HiloSequenceDefaults);
        }

        public ISequence Hilo(Type documentType, HiloSettings settings)
        {
            return _sequences.GetOrAdd(GetSequenceName(documentType, settings),
                sequence => new HiloSequence(_tenant, _options, sequence, settings));
        }

        private string GetSequenceName(Type documentType, HiloSettings settings)
        {
            if (!string.IsNullOrEmpty(settings.SequenceName))
                return settings.SequenceName!;
            return documentType.Name;
        }

        public override string ToString()
        {
            return "Hilo Sequence Factory";
        }
    }
}
