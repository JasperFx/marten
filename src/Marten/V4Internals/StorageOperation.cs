using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Schema.Identity;
using Marten.Services;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.V4Internals
{
    public abstract class StorageOperation<T, TId> : IStorageOperation
    {
        private readonly T _document;
        private readonly TId _id;
        private readonly Dictionary<TId, Guid> _versions;
        protected Guid _version;

        public StorageOperation(T document, TId id, Dictionary<TId, Guid> versions)
        {
            _document = document;
            _id = id;
            _versions = versions;
        }


        // TODO -- improve Lamar to make it possible to use protected members
        public abstract string CommandText();

        public abstract NpgsqlDbType DbType();

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            var parameters = builder.AppendWithParameters(CommandText());
            parameters[0].NpgsqlDbType = DbType();
            parameters[0].Value = _id;



            ConfigureParameters(parameters, _document, session);
        }

        public abstract void ConfigureParameters(NpgsqlParameter[] parameters, T document, IMartenSession session);

        public Type DocumentType => typeof(T);

        public virtual void Postprocess(DbDataReader reader, IList<Exception> exceptions)
        {
            // Nothing
        }

        public virtual Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            return Task.CompletedTask;
        }

        public abstract StorageRole Role();

        protected void setVersionParameter(NpgsqlParameter parameter)
        {
            _version = CombGuidIdGeneration.NewGuid();
            parameter.NpgsqlDbType = NpgsqlDbType.Uuid;
            parameter.Value = _version;
        }

        protected void storeVersion()
        {
            _versions[_id] = _version;
        }

        protected void setCurrentVersionParameter(NpgsqlParameter parameter)
        {
            parameter.NpgsqlDbType = NpgsqlDbType.Uuid;
            if (_versions.TryGetValue(_id, out var version))
            {
                parameter.Value = version;
            }
            else
            {
                parameter.Value = DBNull.Value;
            }
        }

        protected bool postprocessConcurrency(DbDataReader reader, IList<Exception> exceptions)
        {
            var success = false;
            if (reader.Read())
            {
                var version = reader.GetFieldValue<Guid>(0);
                success = version == _version;
            };

            checkVersions(exceptions, success);

            return success;
        }

        protected async Task<bool> postprocessConcurrencyAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            var success = false;
            if (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                var version = await reader.GetFieldValueAsync<Guid>(0, token).ConfigureAwait(false);
                success = version == _version;
            };

            checkVersions(exceptions, success);

            return success;
        }

        private void checkVersions(IList<Exception> exceptions, bool success)
        {
            if (success)
            {
                storeVersion();
            }
            else
            {
                exceptions.Add(new ConcurrencyException(typeof(T), _id));
            }
        }
    }


}
