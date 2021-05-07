using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Baseline.Exceptions;
using Marten.Exceptions;
using Marten.Internal.DirtyTracking;
using Weasel.Postgresql;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Services;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Internal.Operations
{
    public abstract class StorageOperation<T, TId> : IDocumentStorageOperation, IExceptionTransform
    {
        private string _tableName;
        private const string ExpectedMessage = "23505: duplicate key value violates unique constraint";


        private readonly T _document;
        protected readonly TId _id;
        private readonly Dictionary<TId, Guid> _versions;
        protected Guid _version = CombGuidIdGeneration.NewGuid();

        public StorageOperation(T document, TId id, Dictionary<TId, Guid> versions, DocumentMapping mapping)
        {
            _document = document;
            _id = id;
            _versions = versions;
            _tableName = mapping.TableName.Name;
        }

        public object Document => _document;

        public IChangeTracker ToTracker(IMartenSession session)
        {
            return new ChangeTracker<T>(session, _document);
        }

        public abstract string CommandText();

        public abstract NpgsqlDbType DbType();

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            var parameters = builder.AppendWithParameters(CommandText());
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

        public abstract OperationRole Role();

        protected void setVersionParameter(NpgsqlParameter parameter)
        {
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


        protected void postprocessUpdate(DbDataReader reader, IList<Exception> exceptions)
        {
            if (!reader.Read() || reader.IsDBNull(0))
            {
                exceptions.Add(new NonExistentDocumentException(typeof(T), _id));
            }
        }

        protected async Task postprocessUpdateAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
            {
                exceptions.Add(new NonExistentDocumentException(typeof(T), _id));
            };

            var isNull = await reader.IsDBNullAsync(0, token).ConfigureAwait(false);
            if (isNull)
            {
                exceptions.Add(new NonExistentDocumentException(typeof(T), _id));
            }
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

        public bool TryTransform(Exception original, out Exception transformed)
        {
            transformed = null;

            if (original is MartenCommandException m)
            {
                original = m.InnerException;
            }

            if (original.Message.Contains(ExpectedMessage))
            {
                if (original is PostgresException e)
                {
                    if (e.TableName == _tableName)
                    {
                        transformed = new DocumentAlreadyExistsException(original, typeof(T), _id);
                        return true;
                    }
                }
            }

            return false;
        }

        protected void setStringParameter(NpgsqlParameter parameter, string value)
        {
            parameter.NpgsqlDbType = NpgsqlDbType.Varchar;
            if (value == null)
            {
                parameter.Value = DBNull.Value;
            }
            else
            {
                parameter.Value = value;
            }
        }

        protected void setHeaderParameter(NpgsqlParameter parameter, IMartenSession session)
        {
            parameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
            if (session.Headers == null)
            {
                parameter.Value = DBNull.Value;
            }
            else
            {
                parameter.Value = session.Serializer.ToJson(session.Headers);
            }
        }
    }


}
