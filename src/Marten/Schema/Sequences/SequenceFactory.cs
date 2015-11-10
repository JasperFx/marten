using System;

namespace Marten.Schema.Sequences
{
    public class SequenceFactory : ISequences
    {
        private readonly IDocumentSchemaCreation _creation;
        private readonly CommandRunner _runner;
        private readonly IDocumentSchema _schema;

        public SequenceFactory(IDocumentSchema schema, CommandRunner runner, IDocumentSchemaCreation creation)
        {
            _schema = schema;
            _runner = runner;
            _creation = creation;
        }

        public ISequence HiLo(Type documentType, HiloDef def)
        {
            return new HiLoSequence(_runner, documentType.Name, def);
        }
    }
}