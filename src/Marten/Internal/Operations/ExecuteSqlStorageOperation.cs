using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Services;
using Marten.Storage;
using Npgsql;
using Weasel.Postgresql;

namespace Marten.Internal.Operations
{
    internal class ExecuteSqlStorageOperation: IStorageOperation, NoDataReturnedCall
    {
        private readonly string _commandText;
        private readonly object[] _parameterValues;

        public ExecuteSqlStorageOperation(string commandText, params object[] parameterValues)
        {
            _commandText = commandText.TrimEnd(';');
            _parameterValues = parameterValues;
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            var parameters = builder.AppendWithParameters(_commandText);
            if (parameters.Length != _parameterValues.Length)
            {
                throw new InvalidOperationException(
                    $"Wrong number of parameter values to SQL '{_commandText}', got {_parameterValues.Length} parameters");
            }

            for (var i = 0; i < parameters.Length; i++)
            {
                parameters[i].Value = _parameterValues[i];
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
}
