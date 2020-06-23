using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.V4Internals
{
    public class CastingSelector<T, TRoot>: ISelector<T> where T : TRoot
    {
        private readonly ISelector<TRoot> _inner;

        public CastingSelector(ISelector<TRoot> inner)
        {
            _inner = inner;
        }

        public T Resolve(DbDataReader reader)
        {
            return (T) _inner.Resolve(reader);
        }

        public async Task<T> ResolveAsync(DbDataReader reader, CancellationToken token)
        {
            return (T) (await _inner.ResolveAsync(reader, token));
        }
    }
}
