using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.V4Internals
{
    public interface ISelector
    {

    }

    public interface ISelector<T> : ISelector
    {
        T Resolve(DbDataReader reader);

        Task<T> ResolveAsync(DbDataReader reader, CancellationToken token);
    }

    // Can use this for query only or lightweight if not
    // hierarchical and now needing to track versions
    public class JsonSelector<T> : ISelector<T>
    {
        private readonly ISerializer _serializer;

        protected JsonSelector(ISerializer serializer)
        {
            _serializer = serializer;
        }

        public T Resolve(DbDataReader reader)
        {
            using (var json = reader.GetTextReader(0))
            {
                return _serializer.FromJson<T>(json);
            }
        }

        public Task<T> ResolveAsync(DbDataReader reader, CancellationToken token)
        {
            using (var json = reader.GetTextReader(0))
            {
                var doc = _serializer.FromJson<T>(json);
                return Task.FromResult(doc);
            }
        }
    }




}
