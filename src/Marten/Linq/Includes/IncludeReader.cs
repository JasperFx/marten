using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq.Selectors;

namespace Marten.Linq.Includes
{
    public class IncludeReader<T>: IIncludeReader
    {
        private readonly Action<T> _callback;
        private readonly ISelector<T> _selector;

        public IncludeReader(Action<T> callback, ISelector<T> selector)
        {
            _callback = callback;
            _selector = selector;
        }


        public void Read(DbDataReader reader)
        {
            while (reader.Read())
            {
                var item = _selector.Resolve(reader);
                _callback(item);
            }
        }

        public async Task ReadAsync(DbDataReader reader, CancellationToken token)
        {
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var item = await _selector.ResolveAsync(reader, token).ConfigureAwait(false);
                _callback(item);
            }
        }
    }
}
