using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using Baseline;
using Marten.Linq;
using Marten.Schema;
using Marten.Services;
using Marten.Transforms;
using Npgsql;
using NpgsqlTypes;

namespace Marten.Patching
{

    public class PatchOperation : ICall
    {
        private readonly TransformFunction _transform;
        private readonly IQueryableDocument _document;
        private readonly IWhereFragment _fragment;
        private readonly IDictionary<string, object> _patch;
        public Guid Id = Guid.NewGuid();
        private string _sql;

        public PatchOperation(TransformFunction transform, IQueryableDocument document, IWhereFragment fragment, IDictionary<string, object> patch)
        {
            _transform = transform;
            _document = document;
            _fragment = fragment;
            _patch = patch;
        }

        public void RegisterUpdate(UpdateBatch batch)
        {
            batch.AddCall(cmd =>
            {
                var patchJson = batch.Serializer.ToCleanJson(_patch);
                var patchParam = cmd.AddParameter(patchJson, NpgsqlDbType.Jsonb);
                var @where = _fragment.ToSql(cmd.Command);

                _sql = $"update {_document.Table.QualifiedName} set data = {_transform.Function.QualifiedName}(data, :{patchParam.ParameterName}) where {@where}";

                return this;
            });
        }

        void ICall.WriteToSql(StringBuilder builder)
        {
            builder.Append(_sql);
        }
    }


    public interface IPatchExpression<T>
    {
        void Set<TValue>(Expression<Func<T, TValue>> expression, TValue value);
        void Increment(Expression<Func<T, int>> expression, int increment = 1);
        void Increment(Expression<Func<T, long>> expression, long increment = 1);
        void Increment(Expression<Func<T, double>> expression, double increment = 1);
        void Increment(Expression<Func<T, float>> expression, float increment = 1);
        void Append<TElement>(Expression<Func<T, IEnumerable<TElement>>> expression, TElement element);
        void Insert<TElement>(Expression<Func<T, IEnumerable<TElement>>> expression, TElement element, int index = 0);
        void Rename(string oldName, Expression<Func<T, object>> expression);
    }

    public class PatchExpression<T> : IPatchExpression<T>
    {
        private readonly IDocumentSchema _schema;
        public readonly IDictionary<string, object> Patch = new Dictionary<string, object>();

        public PatchExpression(IWhereFragment fragment, IDocumentSchema schema, IUnitOfWork unitOfWork)
        {
            _schema = schema;
        }

        private void addPatch()
        {
            var transform = _schema.TransformFor("");
        }

        public void Set<TValue>(Expression<Func<T, TValue>> expression, TValue value)
        {
            throw new NotImplementedException();
            addPatch();
        }

        public void Increment(Expression<Func<T, int>> expression, int increment = 1)
        {
            throw new NotImplementedException();
            addPatch();
        }

        public void Increment(Expression<Func<T, long>> expression, long increment = 1)
        {
            throw new NotImplementedException();
            addPatch();
        }

        public void Increment(Expression<Func<T, double>> expression, double increment = 1)
        {
            throw new NotImplementedException();
            addPatch();
        }

        public void Increment(Expression<Func<T, float>> expression, float increment = 1)
        {
            throw new NotImplementedException();
            addPatch();
        }

        public void Append<TElement>(Expression<Func<T, IEnumerable<TElement>>> expression, TElement element)
        {
            throw new NotImplementedException();
            addPatch();
        }

        public void Insert<TElement>(Expression<Func<T, IEnumerable<TElement>>> expression, TElement element, int index = 0)
        {
            throw new NotImplementedException();
            addPatch();
        }

        public void Rename(string oldName, Expression<Func<T, object>> expression)
        {
            throw new NotImplementedException();
            addPatch();
        }

    }
}