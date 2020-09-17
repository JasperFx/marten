using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Internal;
using Marten.Internal.Operations;
using Marten.Linq;
using Marten.Linq.SqlGeneration;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Services;
using Marten.Transforms;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Patching
{
    public class PatchOperation: IStorageOperation, NoDataReturnedCall
    {
        private readonly IQueryableDocument _document;
        private readonly ISqlFragment _fragment;
        private readonly IDictionary<string, object> _patch;
        private readonly ISerializer _serializer;
        private readonly TransformFunction _transform;

        public PatchOperation(TransformFunction transform, IQueryableDocument document, ISqlFragment fragment,
            IDictionary<string, object> patch, ISerializer serializer)
        {
            _transform = transform;
            _document = document;
            _fragment = fragment;
            _patch = patch;
            _serializer = serializer;
        }

        // TODO -- come back and do this with a single command!
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
            var patchParam = builder.AddJsonParameter(_serializer.ToCleanJson(_patch));
            if (_patch.ContainsKey("value"))
            {
                var value = PossiblyPolymorhpic ? _serializer.ToJsonWithTypes(_patch["value"]) : _serializer.ToJson(_patch["value"]);
                var copy = new Dictionary<string, object>();
                foreach (var item in _patch)
                {
                    copy.Add(item.Key, item.Value);
                }
                copy["value"] = VALUE_LOOKUP;

                var patchJson = _serializer.ToJson(copy);
                var replacedValue = patchJson.Replace($"\"{VALUE_LOOKUP}\"", value);

                patchParam = builder.AddJsonParameter(replacedValue);
            }

            var versionParam = builder.AddParameter(CombGuidIdGeneration.NewGuid(), NpgsqlDbType.Uuid);

            builder.Append("update ");
            builder.Append(_document.Table.QualifiedName);
            builder.Append(" as d set data = ");
            builder.Append(_transform.Identifier.QualifiedName);
            builder.Append("(data, :");
            builder.Append(patchParam.ParameterName);
            builder.Append("), ");
            builder.Append(DocumentMapping.LastModifiedColumn);
            builder.Append(" = (now() at time zone 'utc'), ");
            builder.Append(DocumentMapping.VersionColumn);
            builder.Append(" = :");
            builder.Append(versionParam.ParameterName);

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

        public Type DocumentType => _document.DocumentType;

        private void applyUpdates(CommandBuilder builder, ISqlFragment where)
        {
            var fields = _document.DuplicatedFields;
            if (!fields.Any())
                return;

            builder.Append(";update ");
            builder.Append(_document.Table.QualifiedName);
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
