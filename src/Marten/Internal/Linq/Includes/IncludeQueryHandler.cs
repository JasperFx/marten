using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Util;

namespace Marten.Internal.Linq.Includes
{
    public class IncludeQueryHandler<T>: IQueryHandler<T>
    {
        private readonly IQueryHandler<T> _inner;
        private readonly IIncludeReader[] _readers;

        public IncludeQueryHandler(IQueryHandler<T> inner, IIncludeReader[] readers)
        {
            _inner = inner;
            _readers = readers;
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            _inner.ConfigureCommand(builder, session);
        }

        public T Handle(DbDataReader reader, IMartenSession session)
        {
            // TODO -- watch this. May be extra temp tables

            foreach (var includeReader in _readers)
            {
                includeReader.Read(reader);
                reader.NextResult();
            }

            return _inner.Handle(reader, session);
        }

        public async Task<T> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            foreach (var includeReader in _readers)
            {
                await includeReader.ReadAsync(reader, token).ConfigureAwait(false);
            }

            // Advance to the last reader for the actual query results
            await reader.NextResultAsync(token).ConfigureAwait(false);
            return await _inner.HandleAsync(reader, session, token).ConfigureAwait(false);
        }
    }
}
