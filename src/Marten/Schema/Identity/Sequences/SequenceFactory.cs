using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Marten.Storage;

namespace Marten.Schema.Identity.Sequences
{
    public class SequenceFactory : ISequences, IFeatureSchema
    {
        private readonly StoreOptions _options;
        private readonly ITenant _tenant;
        private readonly ConcurrentDictionary<Type, ISequence> _sequences = new ConcurrentDictionary<Type, ISequence>();

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

        public bool IsActive { get; set; }

        public ISchemaObject[] Objects
        {
            get
            {
                var table = new Table(new DbObjectName(_options.DatabaseSchemaName, "mt_hilo"));
                table.AddPrimaryKey(new TableColumn("entity_name", "varchar"));
                table.AddColumn("hi_value", "bigint", "default 0");

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
        public void WritePermissions(DdlRules rules, StringWriter writer)
        {
            // Nothing
        }

        public ISequence SequenceFor(Type documentType)
        {
            return _sequences.GetOrAdd(documentType, type =>
            {
                var settings = _options.Storage.MappingFor(type).HiloSettings ?? _options.HiloSequenceDefaults;
                return new HiloSequence(_tenant, _options, documentType.Name, settings);
            });
        }

        public ISequence Hilo(Type documentType, HiloSettings settings)
        {
            return _sequences.GetOrAdd(documentType,
                type => new HiloSequence(_tenant, _options, documentType.Name, settings));
        }


        public override string ToString()
        {
            return "Hilo Sequence Factory";
        }
    }
}