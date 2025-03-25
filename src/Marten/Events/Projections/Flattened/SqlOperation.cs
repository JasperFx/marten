using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Storage;
using Weasel.Postgresql;

namespace Marten.Events.Projections.Flattened;

internal class SqlOperation: IStorageOperation
{
    private readonly string _sql;
    private readonly IEvent _source;
    private readonly IParameterSetter<IEvent>[] _parameterSetters;

    public SqlOperation(string sql, IEvent source, IParameterSetter<IEvent>[] parameterSetters)
    {
        _sql = sql;
        _source = source;
        _parameterSetters = parameterSetters;
    }

    public void ConfigureCommand(ICommandBuilder builder, IMartenSession session)
    {
        var parameters = builder.AppendWithParameters(_sql);
        for (int i = 0; i < _parameterSetters.Length; i++)
        {
            _parameterSetters[i].SetValue(parameters[i], _source);
        }
    }

    public Type DocumentType => typeof(StorageFeatures);
    public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
    {
        // nothing
    }

    public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
    {
        return Task.CompletedTask;
    }

    public OperationRole Role()
    {
        return OperationRole.Other;
    }
}