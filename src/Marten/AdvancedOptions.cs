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
        /// Dumps all of the dynamic storage code that matches the current Marten configuration
        /// </summary>
        /// <param name="file"></param>
        public void WriteStorageCode(string file)
        {
            var code = DocumentStorageBuilder.GenerateDocumentStorageCode(Options.AllDocumentMappings.ToArray());
            new FileSystem().WriteStringToFile(file, code);
        }

        /// <summary>
        /// Directly open a managed connection to the underlying Postgresql database
        /// </summary>
        /// <param name="mode"></param>
        /// <param name="isolationLevel"></param>
        /// <returns></returns>
        public IManagedConnection OpenConnection(CommandRunnerMode mode = CommandRunnerMode.ReadOnly, IsolationLevel isolationLevel = IsolationLevel.ReadUncommitted)
        {
            return new ManagedConnection(Options.ConnectionFactory(), mode, isolationLevel);
        }

        public UpdateBatch CreateUpdateBatch()
        {
            return new UpdateBatch(Options, _serializer, OpenConnection());
        }

        public UnitOfWork CreateUnitOfWork()
        {
            return new UnitOfWork(_schema, new MartenExpressionParser(_serializer, Options));
        }

        public IList<IDocumentStorage> PrecompileAllStorage()
        {
            return Options.AllDocumentMappings.Select(x => _schema.StorageFor(x.DocumentType)).ToList();
        }
    }
}