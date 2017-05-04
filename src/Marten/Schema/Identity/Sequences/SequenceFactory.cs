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
        private readonly ConcurrentDictionary<Type, ISequence> _sequences = new ConcurrentDictionary<Type, ISequence>();

        public SequenceFactory(StoreOptions options)
        {
            _options = options;
        }

        private DbObjectName Table => new DbObjectName(_options.DatabaseSchemaName, "mt_hilo");


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
            // Okay to let it blow up if it doesn't exist here IMO
            return _sequences[documentType];
        }

        public ISequence Hilo(Type documentType, HiloSettings settings)
        {
            return _sequences.GetOrAdd(documentType,
                type => new HiloSequence(_options.ConnectionFactory, _options, documentType.Name, settings));
        }


        public override string ToString()
        {
            return "Hilo Sequence Factory";
        }
    }
}