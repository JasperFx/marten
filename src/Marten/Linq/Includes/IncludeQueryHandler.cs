using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Linq.QueryHandlers;
using Marten.Util;

namespace Marten.Linq.Includes
{
    public interface IIncludeQueryHandler<T>
    {
        IQueryHandler<T> Inner { get; }
    }

    public class IncludeQueryHandler<T>: IQueryHandler<T>, IIncludeQueryHandler<T>
    {
        private readonly IIncludeReader[] _readers;

        public IncludeQueryHandler(IQueryHandler<T> inner, IIncludeReader[] readers)
        {
            Inner = inner;
            _readers = readers;
        }

        public IQueryHandler<T> Inner { get; }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            Inner.ConfigureCommand(builder, session);
        }

        public T Handle(DbDataReader reader, IMartenSession session)
        {
            // TODO -- watch this. May be extra temp tables

            foreach (var includeReader in _readers)
            {
                includeReader.Read(reader);
                reader.NextResult();
            }

            return Inner.Handle(reader, session);
        }

        public async Task<T> HandleAsync(DbDataReader reader, IMartenSession session, CancellationToken token)
        {
            // TODO -- watch this. May be extra temp tables

            foreach (var includeReader in _readers)
            {
                await includeReader.ReadAsync(reader, token);
                await reader.NextResultAsync(token);
            }

            return await Inner.HandleAsync(reader, session, token);
        }
    }
}
