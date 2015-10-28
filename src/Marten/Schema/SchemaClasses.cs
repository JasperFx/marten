using System;
using FubuCore;
using FubuCore.CommandLine;
using Marten.Generation;
using Npgsql;

namespace Marten.Schema
{
    public interface IDocumentCleaner
    {
        void AllDocuments();
        void DocumentsFor(Type documentType);
        void DocumentsExcept(params Type[] documentTypes);

        void CompletelyRemove(Type documentType);
    }

    public class DevelopmentDocumentCleaner : IDocumentCleaner
    {
        private readonly CommandRunner _runner;

        public DevelopmentDocumentCleaner(IConnectionFactory factory)
        {
            _runner = new CommandRunner(factory);
        }

        public void AllDocuments()
        {
            throw new NotImplementedException();
        }

        public void DocumentsFor(Type documentType)
        {
            var tableName = SchemaBuilder.TableNameFor(documentType);
            var sql = "truncate {0} cascade".ToFormat(tableName);

            _runner.Execute(sql);
        }

        public void DocumentsExcept(params Type[] documentTypes)
        {
            throw new NotImplementedException();
        }

        public void CompletelyRemove(Type documentType)
        {
            _runner.Execute("DROP TABLE IF EXISTS {0} CASCADE;".ToFormat(SchemaBuilder.TableNameFor(documentType)));

            // TODO -- later, this gets more complicated and you'll need to do it via the IDocumentSchema
            _runner.Execute("DROP FUNCTION if exists {0}(docId UUID, doc JSON)".ToFormat(SchemaBuilder.UpsertNameFor(documentType)));
        }
    }


}