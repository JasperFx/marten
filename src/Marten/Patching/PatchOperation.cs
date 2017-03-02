using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Marten.Linq;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Services;
using Marten.Transforms;
using NpgsqlTypes;
using Baseline;

namespace Marten.Patching
{
    public class PatchOperation : IStorageOperation, NoDataReturnedCall
    {
        private readonly IQueryableDocument _document;
        private readonly IWhereFragment _fragment;
        private readonly IDictionary<string, object> _patch;
        private readonly TransformFunction _transform;
        private string _sql;
        private string _where;

        public PatchOperation(TransformFunction transform, IQueryableDocument document, IWhereFragment fragment,
            IDictionary<string, object> patch)
        {
            _transform = transform;
            _document = document;
            _fragment = fragment;
            _patch = patch;
        }

        void ICall.WriteToSql(StringBuilder builder)
        {
            builder.Append(_sql);
        }

        public void AddParameters(IBatchCommand batch)
        {
            var patchJson = batch.Serializer.ToCleanJson(_patch);
            var patchParam = batch.AddParameter(patchJson, NpgsqlDbType.Jsonb);
            var versionParam = batch.AddParameter(CombGuidIdGeneration.NewGuid(), NpgsqlDbType.Uuid);

            _where = _fragment.ToSql(batch.Command);
            if (!_where.StartsWith("where "))
                _where = "where " + _where;

            _sql = $@"
update {_document.Table.QualifiedName} as d 
set data = {_transform.Function.QualifiedName}(data, :{patchParam.ParameterName}), {DocumentMapping.LastModifiedColumn} = (now() at time zone 'utc'), {DocumentMapping.VersionColumn} = :{versionParam.ParameterName}
{_where}";

        }

        public IStorageOperation UpdateDuplicateFieldOperation()
        {
            return new UpdateDuplicateFields(this);
        }

        public Type DocumentType => _document.DocumentType;

        public class UpdateDuplicateFields : IStorageOperation
        {
            private readonly PatchOperation _parent;

            public UpdateDuplicateFields(PatchOperation parent)
            {
                _parent = parent;
            }

            public void WriteToSql(StringBuilder builder)
            {
                var setters = _parent._document.DuplicatedFields.Select(x => x.UpdateSqlFragment()).Join(", ");
                builder.Append($"update {_parent._document.Table.QualifiedName} as d set {setters} {_parent._where}");
            }

            public void AddParameters(IBatchCommand batch)
            {
                // nothing here
            }

            public Type DocumentType => _parent.DocumentType;
        }
    }


}