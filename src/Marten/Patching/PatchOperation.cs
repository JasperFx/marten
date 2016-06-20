using System;
using System.Collections.Generic;
using System.Text;
using Marten.Linq;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Services;
using Marten.Transforms;
using NpgsqlTypes;

namespace Marten.Patching
{
    public class PatchOperation : IStorageOperation
    {
        private readonly IQueryableDocument _document;
        private readonly IWhereFragment _fragment;
        private readonly IDictionary<string, object> _patch;
        private readonly TransformFunction _transform;
        private string _sql;
        public Guid Id = Guid.NewGuid();

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

            var where = _fragment.ToSql(batch.Command);

            _sql = $@"
update {_document.Table.QualifiedName} as d 
set data = {_transform.Function.QualifiedName}(data, :{patchParam.ParameterName}), {DocumentMapping.LastModifiedColumn} = (now() at time zone 'utc'), {DocumentMapping.VersionColumn} = :{versionParam.ParameterName}
where {where}";

        }
    }
}