using System;
using System.Linq;
using Marten.Services;

namespace Marten.Schema.Sequences
{
    public class SequenceFactory : ISequences
    {
        private readonly IDocumentSchemaCreation _creation;
        private readonly IDocumentSchema _schema;
        private readonly IConnectionFactory _factory;

        public SequenceFactory(IDocumentSchema schema, IConnectionFactory factory, IDocumentSchemaCreation creation)
        {
            _schema = schema;
            _factory = factory;
            _creation = creation;
        }

        public ISequence Hilo(Type documentType, HiloSettings settings)
        {
            // TODO -- here, need to see if the mt_hilo table is created, and if not,
            // do it through _creation.

            if (!_schema.SchemaTableNames().Contains("mt_hilo"))
            {
                _creation.RunScript("mt_hilo");
            }

            return new HiloSequence(_factory, documentType.Name, settings);
        }
    }
}