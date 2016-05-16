using System.Collections.Generic;
using System.Data;
using System.Linq;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;

namespace Marten
{
    public class AdvancedOptions
    {
        private readonly ISerializer _serializer;
        private readonly IDocumentSchema _schema;

        /// <summary>
        /// The original StoreOptions used to configure the current DocumentStore
        /// </summary>
        public StoreOptions Options { get; }

        public AdvancedOptions(IDocumentCleaner cleaner, StoreOptions options, ISerializer serializer, IDocumentSchema schema)
        {
            _serializer = serializer;
            _schema = schema;
            Options = options;
            Clean = cleaner;
        }

        /// <summary>
        /// Used to remove document data and tables from the current Postgresql database
        /// </summary>
        public IDocumentCleaner Clean { get; }


        /// <summary>
        /// Directly open a managed connection to the underlying Postgresql database
        /// </summary>
        /// <param name="mode"></param>
        /// <param name="isolationLevel"></param>
        /// <returns></returns>
        public IManagedConnection OpenConnection(CommandRunnerMode mode = CommandRunnerMode.AutoCommit, IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
        {
            return new ManagedConnection(Options.ConnectionFactory(), mode, isolationLevel);
        }

        /// <summary>
        /// Creates an UpdateBatch object for low level batch updates
        /// </summary>
        /// <returns></returns>
        public UpdateBatch CreateUpdateBatch()
        {
            return new UpdateBatch(Options, _serializer, OpenConnection());
        }

        /// <summary>
        /// Creates a new Marten UnitOfWork that could be used to express
        /// a transaction
        /// </summary>
        /// <returns></returns>
        public UnitOfWork CreateUnitOfWork()
        {
            return new UnitOfWork(_schema);
        }

        /// <summary>
        /// Compiles all of the IDocumentStorage classes upfront for all known document types
        /// </summary>
        /// <returns></returns>
        public IList<IDocumentStorage> PrecompileAllStorage()
        {
            return Options.AllDocumentMappings.Select(x => _schema.StorageFor(x.DocumentType)).ToList();
        }
    }
}