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
        private readonly Guid _newVersion;
        private readonly Guid _oldVersion;

        public OptimisticConcurrencyCallback(object id, VersionTracker versions, Guid newVersion, Guid oldVersion)
        {
            _id = id;
            _versions = versions;
            _newVersion = newVersion;
            _oldVersion = oldVersion;
        }

        public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
        {
            var success = false;
            if (reader.Read())
            {
                var version = reader.GetFieldValue<Guid>(0);
                success = version == _newVersion;
            }

            if (success)
            {
                _versions.Store<T>(_id, _newVersion);                
            }
            else
            {
                exceptions.Add(new ConcurrencyException(typeof(T), _id));
            }
        }

        public async Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            var success = false;
            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var version = await reader.GetFieldValueAsync<Guid>(0, token).ConfigureAwait(false);
                success = version == _newVersion;
            }

            if (success)
            {
                _versions.Store<T>(_id, _newVersion);
            }
            else
            {
                exceptions.Add(new ConcurrencyException(typeof(T), _id));
            }
        }
    }

    public class ConcurrencyException : Exception
    {
        public ConcurrencyException(Type docType, object id) : base($"Optimistic concurrency check failed for {docType.FullName} #{id}")
        {
        }
    }
}