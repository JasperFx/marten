using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Services.BatchQuerying
{
    public class DataReaderAdvancer : IDataReaderHandler
    {
        private readonly IDataReaderHandler _inner;

        public DataReaderAdvancer(IDataReaderHandler inner)
        {
            _inner = inner;
        }

        public async Task Handle(DbDataReader reader, CancellationToken token)
        {
            var hasNext = await reader.NextResultAsync(token).ConfigureAwait(false);

            if (!hasNext)
            {
                throw new InvalidOperationException("There is no next result to read over.");
            }

            await _inner.Handle(reader, token).ConfigureAwait(false);
        }
    }
}