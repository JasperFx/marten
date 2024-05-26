#nullable enable
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
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration;

internal class StatementOperation: Statement, IStorageOperation
{
    private readonly IOperationFragment _operation;
    private readonly IDocumentStorage _storage;

    public StatementOperation(IDocumentStorage storage, IOperationFragment operation)
    {
        _storage = storage;
        _operation = operation;
        DocumentType = storage.SourceType;
    }

    public StatementOperation(IDocumentStorage storage, IOperationFragment operation, ISqlFragment where): this(storage,
        operation)
    {
        Wheres.Add(where);
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        Apply(builder);
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

    public OperationRole Role()
    {
        return _operation.Role();
    }

    protected override void configure(ICommandBuilder sql)
    {
        _operation.Apply(sql);
        writeWhereClause(sql);
    }

    protected void writeWhereClause(ICommandBuilder sql)
    {
        if (Wheres.Any())
        {
            sql.Append(" where ");
            Wheres[0].Apply(sql);
            for (var i = 1; i < Wheres.Count; i++)
            {
                sql.Append(" and ");
                Wheres[i].Apply(sql);
            }
        }
    }

    public ISqlFragment ApplyFiltering<T>(DocumentSessionBase session, Expression<Func<T, bool>> expression)
    {
        Expression body = expression;
        if (expression is LambdaExpression l)
        {
            body = l.Body;
        }

        ParseWhereClause(new[] { body }, session, _storage.QueryMembers, _storage);

        return Wheres.SingleOrDefault();
    }
}
