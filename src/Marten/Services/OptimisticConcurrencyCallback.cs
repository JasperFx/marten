using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Marten.Services
{
    public class OptimisticConcurrencyCallback<T> : ICallback
    {
        private readonly object _id;
        private readonly VersionTracker _versions;

        public OptimisticConcurrencyCallback(object id, VersionTracker versions)
        {
            _id = id;
            _versions = versions;
        }

        public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
        {
            var success = false;
            long newVersion = -1;

            if (reader.Read())
            {
                newVersion = reader.GetFieldValue<long>(0);
                success = newVersion > 0;
            }

            if (success)
            {
                _versions.Store<T>(_id, newVersion);                
            }
            else
            {
                exceptions.Add(new ConcurrencyException(typeof(T), _id));
            }
        }

        public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            var success = false;
            long newVersion = -1;

            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                newVersion = await reader.GetFieldValueAsync<long>(0, token).ConfigureAwait(false);
                success = newVersion > 0;
            }

            if (success)
            {
                _versions.Store<T>(_id, newVersion);
            }
            else
            {
                exceptions.Add(new ConcurrencyException(typeof(T), _id));
            }
        }
    }
}