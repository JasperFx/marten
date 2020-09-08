using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Sessions;
using Marten.Internal.Storage;
using Marten.Linq.Parsing;
using Marten.Util;
using Remotion.Linq.Clauses;

namespace Marten.Linq.SqlGeneration
{
    public class StatementOperation : DocumentStatement, IStorageOperation
    {
        private readonly IOperationFragment _operation;

        public StatementOperation(IDocumentStorage storage, IOperationFragment operation) : base(storage)
        {
            _operation = operation;
            DocumentType = storage.SourceType;
        }

        protected override void configure(CommandBuilder builder)
        {
            _operation.Apply(builder);
            writeWhereClause(builder);
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            configure(builder);
        }

        public Type DocumentType { get; }
        public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
        {
            // Nothing
        }

        public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            return Task.CompletedTask;
        }

        public OperationRole Role() => _operation.Role();

        public void ApplyFiltering<T>(DocumentSessionBase session, Expression<Func<T, bool>> expression)
        {
            var queryExpression = session.Query<T>().Where(expression).Expression;
            var model = MartenQueryParser.Flyweight.GetParsedQuery(queryExpression);
            var where = model.BodyClauses.OfType<WhereClause>().Single();
            WhereClauses.Add(where);
            CompileLocal(session);
        }
    }
}
