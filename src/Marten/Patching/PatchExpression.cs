using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Internal.Sessions;
using Marten.Linq;
using Marten.Linq.Parsing;
using Marten.Linq.SqlGeneration;
using Marten.Util;

namespace Marten.Patching
{
    public class PatchExpression<T>: IPatchExpression<T>
    {
        private readonly ISqlFragment _filter;
        private readonly Expression<Func<T, bool>> _filterExpression;
        private readonly DocumentSessionBase _session;
        public readonly IDictionary<string, object> Patch = new Dictionary<string, object>();

        public PatchExpression(ISqlFragment filter, DocumentSessionBase session)
        {
            _filter = filter;
            _session = session;
        }

        public PatchExpression(Expression<Func<T, bool>> filterExpression, DocumentSessionBase session)
        {
            _filterExpression = filterExpression;
            _session = session;
        }

        public void Set<TValue>(string name, TValue value)
        {
            set(name, value);
        }

        public void Set<TParent, TValue>(string name, Expression<Func<T, TParent>> expression, TValue value)
        {
            set(toPath(expression) + $".{name}", value);
        }

        public void Set<TValue>(Expression<Func<T, TValue>> expression, TValue value)
        {
            set(toPath(expression), value);
        }

        public void Duplicate<TElement>(Expression<Func<T, TElement>> expression,
            params Expression<Func<T, TElement>>[] destinations)
        {
            if (destinations.Length == 0)
                throw new ArgumentException("At least one destination must be given");

            Patch.Add("type", "duplicate");
            Patch.Add("path", toPath(expression));
            Patch.Add("targets", destinations.Select(toPath).ToArray());

            apply();
        }

        public void Increment(Expression<Func<T, int>> expression, int increment = 1)
        {
            Patch.Add("type", "increment");
            Patch.Add("increment", increment);
            Patch.Add("path", toPath(expression));

            apply();
        }

        public void Increment(Expression<Func<T, long>> expression, long increment = 1)
        {
            Patch.Add("type", "increment");
            Patch.Add("increment", increment);
            Patch.Add("path", toPath(expression));

            apply();
        }

        public void Increment(Expression<Func<T, double>> expression, double increment = 1)
        {
            Patch.Add("type", "increment_float");
            Patch.Add("increment", increment);
            Patch.Add("path", toPath(expression));

            apply();
        }

        public void Increment(Expression<Func<T, float>> expression, float increment = 1)
        {
            Patch.Add("type", "increment_float");
            Patch.Add("increment", increment);
            Patch.Add("path", toPath(expression));

            apply();
        }

        public void Append<TElement>(Expression<Func<T, IEnumerable<TElement>>> expression, TElement element)
        {
            Patch.Add("type", "append");
            Patch.Add("value", element);
            Patch.Add("path", toPath(expression));

            apply();
        }

        public void AppendIfNotExists<TElement>(Expression<Func<T, IEnumerable<TElement>>> expression, TElement element)
        {
            Patch.Add("type", "append_if_not_exists");
            Patch.Add("value", element);
            Patch.Add("path", toPath(expression));

            apply();
        }

        public void Insert<TElement>(Expression<Func<T, IEnumerable<TElement>>> expression, TElement element,
            int index = 0)
        {
            Patch.Add("type", "insert");
            Patch.Add("value", element);
            Patch.Add("path", toPath(expression));
            Patch.Add("index", index);

            apply();
        }

        public void InsertIfNotExists<TElement>(Expression<Func<T, IEnumerable<TElement>>> expression, TElement element,
            int index = 0)
        {
            Patch.Add("type", "insert_if_not_exists");
            Patch.Add("value", element);
            Patch.Add("path", toPath(expression));
            Patch.Add("index", index);

            apply();
        }

        public void Remove<TElement>(Expression<Func<T, IEnumerable<TElement>>> expression, TElement element,
            RemoveAction action = RemoveAction.RemoveFirst)
        {
            Patch.Add("type", "remove");
            Patch.Add("value", element);
            Patch.Add("path", toPath(expression));
            Patch.Add("action", (int)action);

            apply();
        }

        public void Rename(string oldName, Expression<Func<T, object>> expression)
        {
            Patch.Add("type", "rename");

            var newPath = toPath(expression);
            var parts = newPath.Split('.');

            var to = parts.Last();
            parts[parts.Length - 1] = oldName;

            var path = parts.Join(".");

            Patch.Add("to", to);
            Patch.Add("path", path);

            apply();
        }

        public void Delete(string name)
        {
            delete(name);
        }

        public void Delete<TParent>(string name, Expression<Func<T, TParent>> expression)
        {
            delete(toPath(expression) + $".{name}");
        }

        public void Delete<TElement>(Expression<Func<T, TElement>> expression)
        {
            delete(toPath(expression));
        }

        private void set<TValue>(string path, TValue value)
        {
            Patch.Add("type", "set");
            Patch.Add("value", value);
            Patch.Add("path", path);

            apply();
        }

        private void delete(string path)
        {
            Patch.Add("type", "delete");
            Patch.Add("path", path);

            apply();
        }

        private string toPath(Expression expression)
        {
            var visitor = new FindMembers();
            visitor.Visit(expression);

            // TODO -- don't like this. Smells like duplication in logic
            return visitor.Members.Select(x => x.Name.FormatCase(_session.Serializer.Casing)).Join(".");
        }

        private void apply()
        {
            var transform = _session.Tenant.TransformFor(StoreOptions.PatchDoc);
            var storage = _session.StorageFor(typeof(T));

            ISqlFragment where;
            if (_filter == null)
            {
                var statement = new StatementOperation(storage, null);
                statement.ApplyFiltering(_session, _filterExpression);

                where = statement.Where;
            }
            else
            {
                where = storage.FilterDocuments(null, _filter);
            }

            var operation = new PatchOperation(transform, storage.QueryableDocument, where, Patch, _session.Serializer);

            _session.QueueOperation(operation);
        }
    }
}
