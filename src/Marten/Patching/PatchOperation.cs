using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Linq;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Services;
using Marten.Transforms;
using Marten.Util;
using NpgsqlTypes;

namespace Marten.Patching
{
    public class PatchOperation : IStorageOperation, NoDataReturnedCall
    {
        private readonly IQueryableDocument _document;
        private readonly IWhereFragment _fragment;
        private readonly IDictionary<string, object> _patch;
        private readonly ISerializer _serializer;
        private readonly TransformFunction _transform;

        public PatchOperation(TransformFunction transform, IQueryableDocument document, IWhereFragment fragment,
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
        public void ConfigureCommand(CommandBuilder builder)
        {
            var patchParam = builder.AddJsonParameter(_serializer.ToCleanJson(_patch));
            if (_patch.ContainsKey("value"))
            {
                var value = _serializer.ToJson(_patch["value"]);
                var copy = new Dictionary<string, object>();
                foreach (var item in _patch)
                {
                    copy.Add(item.Key, item.Value);
                }
                copy["value"] = VALUE_LOOKUP;

                var patchJson = _serializer.ToCleanJson(copy);
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

        private void applyUpdates(CommandBuilder builder, IWhereFragment where)
        {
            var fields = _document.DuplicatedFields;
            if (!fields.Any()) return;

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