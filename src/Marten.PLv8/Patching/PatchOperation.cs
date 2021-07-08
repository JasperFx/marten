using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Internal.Storage;
using Marten.PLv8.Transforms;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Services;
using NpgsqlTypes;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.PLv8.Patching
{
    public class PatchOperation: IStorageOperation, NoDataReturnedCall
    {
        private readonly IDocumentStorage _storage;
        private readonly ISqlFragment _fragment;
        private readonly IDictionary<string, object> _patch;
        private readonly ISerializer _serializer;
        private readonly TransformFunction _transform;

        public PatchOperation(TransformFunction transform, IDocumentStorage storage, ISqlFragment fragment,
            IDictionary<string, object> patch, ISerializer serializer)
        {
            _transform = transform;
            _storage = storage;
            _fragment = fragment;
            _patch = patch;
            _serializer = serializer;
        }

        private const string VALUE_LOOKUP = "___VALUE___";

        internal bool PossiblyPolymorhpic { get; set; } = false;

        public void Postprocess(DbDataReader reader, IList<Exception> exceptions)
        {
            // Nothing
        }

        public Task PostprocessAsync(DbDataReader reader, IList<Exception> exceptions, CancellationToken token)
        {
            return Task.CompletedTask;
        }

        public OperationRole Role()
        {
            return OperationRole.Patch;
        }

        public void ConfigureCommand(CommandBuilder builder, IMartenSession session)
        {
            var patchParam = builder.AddParameter(_serializer.ToCleanJson(_patch), NpgsqlDbType.Jsonb);
            if (_patch.TryGetValue("value", out var document))
            {
                var value = PossiblyPolymorhpic ? _serializer.ToJsonWithTypes(document) : _serializer.ToJson(document);
                var copy = new Dictionary<string, object>();
                foreach (var item in _patch)
                {
                    copy.Add(item.Key, item.Value);
                }
                copy["value"] = VALUE_LOOKUP;

                var patchJson = _serializer.ToJson(copy);
                var replacedValue = patchJson.Replace($"\"{VALUE_LOOKUP}\"", value);

                patchParam = builder.AddParameter(replacedValue, NpgsqlDbType.Jsonb);
            }

            builder.Append("update ");
            builder.Append(_storage.TableName.QualifiedName);
            builder.Append(" as d set data = ");
            builder.Append(_transform.Identifier.QualifiedName);
            builder.Append("(data, :");
            builder.Append(patchParam.ParameterName);
            builder.Append("), ");
            builder.Append(SchemaConstants.LastModifiedColumn);
            builder.Append(" = (now() at time zone 'utc'), ");
            builder.Append(SchemaConstants.VersionColumn);
            builder.Append(" = ");
            builder.AppendParameter(CombGuidIdGeneration.NewGuid());

            if (!_fragment.Contains("where"))
            {
                builder.Append(" where ");
            }
            else
            {
                builder.Append(" ");
            }

            _fragment.Apply(builder);

            applyUpdates(builder, _fragment);
        }

        public Type DocumentType => _storage.DocumentType;

        private void applyUpdates(CommandBuilder builder, ISqlFragment where)
        {
            var fields = _storage.DuplicatedFields;
            if (!fields.Any())
                return;

            builder.Append(";update ");
            builder.Append(_storage.TableName.QualifiedName);
            builder.Append(" as d set ");

            builder.Append(fields[0].UpdateSqlFragment());
            for (var i = 1; i < fields.Length; i++)
            {
                builder.Append(", ");
                builder.Append(fields[i].UpdateSqlFragment());
            }

            builder.Append(" where ");
            where.Apply(builder);
        }
    }
}
