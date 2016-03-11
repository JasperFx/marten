using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Npgsql;

namespace Marten.Linq
{
    public interface ISelector<T>
    {
        IEnumerable<T> Execute(NpgsqlCommand command, IManagedConnection connection, IDocumentSchema schema, IIdentityMap identityMap);

        Task<IEnumerable<T>> ExecuteAsync(NpgsqlCommand command, IManagedConnection connection, IDocumentSchema schema, IIdentityMap identityMap,
            CancellationToken token);

        string SelectClause(IDocumentMapping mapping);
    }

    public class WholeDocumentSelector<T> : ISelector<T>
    {
        public IEnumerable<T> Execute(NpgsqlCommand command, IManagedConnection connection, IDocumentSchema schema, IIdentityMap identityMap)
        {
            return connection.Resolve(command, schema.StorageFor(typeof (T)).As<IResolver<T>>(), identityMap);
        }

        public async Task<IEnumerable<T>> ExecuteAsync(NpgsqlCommand command, IManagedConnection connection, IDocumentSchema schema, IIdentityMap identityMap,
            CancellationToken token)
        {
            return await connection.ResolveAsync(command, schema.StorageFor(typeof(T)).As<IResolver<T>>(), identityMap, token).ConfigureAwait(false);
        }

        public string SelectClause(IDocumentMapping mapping)
        {
            return mapping.SelectFields("d");
        }
    }
}