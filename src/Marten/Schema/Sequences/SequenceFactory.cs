using System;
using System.Linq;

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

        public ISequence HiLo(Type documentType, HiloDef def)
        {
            // TODO -- here, need to see if the mt_hilo table is created, and if not,
            // do it through _creation.

            if (!_schema.SchemaTableNames().Contains("mt_hilo"))
            {
                _creation.RunScript("mt_hilo");
            }

            return new HiLoSequence(_runner, documentType.Name, def);
        }
    }
}