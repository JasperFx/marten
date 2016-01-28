using System;
using System.Linq;
using Marten.Services;

namespace Marten.Schema.Sequences
{
    public class SequenceFactory : ISequences
    {
        private readonly IDocumentSchemaCreation _creation;
        private readonly ICommandRunner _runner;
        private readonly IDocumentSchema _schema;

        public SequenceFactory(IDocumentSchema schema, ICommandRunner runner, IDocumentSchemaCreation creation)
        {
            _schema = schema;
            _runner = runner;
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

            return new HiloSequence(_runner, documentType.Name, settings);
        }
    }
}