using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Marten.Util;
using Npgsql;
using Remotion.Linq.Clauses;

namespace Marten.V4Internals
{
    public interface IMartenSession : IDisposable
    {
        ISerializer Serializer { get; }
        Dictionary<Type, object> ItemMap { get; }
        ITenant Tenant { get; }

        VersionTracker Versions { get; }

        IDatabase Database { get; }
        IDocumentStorage StorageFor(Type documentType);

        StoreOptions Options { get; }
        TResult Execute<TResult>(Expression expression);
        Task<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken token);

        TResult Execute<TResult>(Expression expression, ResultOperatorBase op);
        Task<TResult> ExecuteAsync<TResult>(Expression expression, CancellationToken token, ResultOperatorBase op);
    }

    internal static class MartenSessionExtensions
    {
        internal static void RunOperations(this IMartenSession session, IList<IStorageOperation> operations)
        {
            var command = new NpgsqlCommand();
            var builder = new CommandBuilder(command);
            foreach (var operation in operations)
            {
                operation.ConfigureCommand(builder, session);
                builder.Append(";");
            }

            var exceptions = new List<Exception>();

            // TODO -- hokey!
            command.CommandText = builder.ToString();
            using (var reader = session.Database.ExecuteReader(command))
            {
                operations[0].Postprocess(reader, exceptions);
                for (int i = 1; i < operations.Count; i++)
                {
                    reader.NextResult();
                    operations[i].Postprocess(reader, exceptions);
                }
            }

            if (exceptions.Any())
            {
                throw new AggregateException(exceptions);
            }
        }
    }
}
