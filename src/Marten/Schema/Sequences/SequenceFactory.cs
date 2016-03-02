using System;
using System.Linq;
using Marten.Services;

namespace Marten.Schema.Sequences
{
    public class SequenceFactory : ISequences
    {
        private readonly IDocumentSchema _schema;
        private readonly IConnectionFactory _factory;
        private readonly StoreOptions _options;

        public SequenceFactory(IDocumentSchema schema, IConnectionFactory factory, StoreOptions options)
        {
            _schema = schema;
            _factory = factory;
            _options = options;
        }

        public ISequence Hilo(Type documentType, HiloSettings settings)
        {
            if (!_schema.SchemaTableNames().Contains("mt_hilo"))
            {
                if (_options.AutoCreateSchemaObjects == AutoCreate.None)
                {
                    throw new InvalidOperationException($"Hilo table is missing, but {nameof(StoreOptions.AutoCreateSchemaObjects)} is {_options.AutoCreateSchemaObjects}");
                }


                _factory.RunSql(SchemaBuilder.GetText("mt_hilo"));
            }

            return new HiloSequence(_factory, documentType.Name, settings);
        }
    }
}