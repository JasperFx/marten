using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Exceptions;

namespace Marten.Services
{
    public class OptimisticConcurrencyCallback<T>: ICallback
    {
        private readonly ConcurrencyChecks _mode;
        private readonly object _id;
        private readonly VersionTracker _versions;
        private readonly Guid _newVersion;
        private readonly Guid? _oldVersion;
        private readonly Action<Guid> _setVersion;

        public OptimisticConcurrencyCallback(ConcurrencyChecks mode, object id, VersionTracker versions, Guid newVersion, Guid? oldVersion, Action<Guid> setVersion)
        {
            _mode = mode;
            _id = id;
            _versions = versions;
            _newVersion = newVersion;
            _oldVersion = oldVersion;
            _setVersion = setVersion;
        }

        public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
        {
            var success = false;
            if (reader.Read())
            {
                var version = reader.GetFieldValue<Guid>(0);
                success = version == _newVersion;
            };

            checkAndStoreVersions(exceptions, success);
        }

        private void checkAndStoreVersions(IList<Exception> exceptions, bool success)
        {
            if (_mode == ConcurrencyChecks.Enabled)
            {
                if (success)
                {
                    _setVersion(_newVersion);
                    _versions.Store<T>(_id, _newVersion);
                }
                else
                {
                    _setVersion(_oldVersion ?? Guid.Empty);

                    exceptions.Add(new ConcurrencyException(typeof(T), _id));
                }
            }
            else
            {
                _setVersion(_newVersion);
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

            checkAndStoreVersions(exceptions, success);
        }
    }
}
