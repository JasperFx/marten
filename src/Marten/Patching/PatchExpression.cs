using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using JasperFx.Core;
using Marten.Internal.Sessions;
using Marten.Linq.Parsing;
using Marten.Util;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

#nullable enable
namespace Marten.Patching;

internal record PatchData(IDictionary<string, object> Items, bool PossiblyPolymorphic);

internal class PatchExpression<T>: IPatchExpression<T>
{
    private readonly DocumentSessionBase _session;
    private readonly List<PatchData> _patchSet = new();
    internal IDictionary<string, object>? Patch => _patchSet.Count > 0
        ? _patchSet[^1].Items
        : null;

    public PatchExpression(ISqlFragment filter, DocumentSessionBase session)
    {
        _session = session;
        var storage = _session.StorageFor(typeof(T));
        var operation = new PatchOperation(session, PatchFunction, storage, _patchSet, _session.Serializer);
        if (filter != null)
        {
            operation.Wheres.Add(storage.FilterDocuments(filter, _session));
        }
        else
        {
            operation.Wheres.Add(storage.DefaultWhereFragment());
        }
        _session.QueueOperation(operation);
    }

    public PatchExpression(Expression<Func<T, bool>> filterExpression, DocumentSessionBase session)
    {
        _session = session;
        var storage = _session.StorageFor(typeof(T));
        var operation = new PatchOperation(session, PatchFunction, storage, _patchSet, _session.Serializer);
        if (filterExpression != null)
        {
            operation.ApplyFiltering(_session, filterExpression);
        }
        else
        {
            operation.Wheres.Add(storage.DefaultWhereFragment());
        }
        _session.QueueOperation(operation);
    }

    public IPatchExpression<T> Set<TValue>(string name, TValue value)
    {
        return set(name, value);
    }

    public IPatchExpression<T> Set<TParent, TValue>(string name, Expression<Func<T, TParent>> expression, TValue value)
    {
        return set(toPath(expression) + $".{name}", value);
    }

    public IPatchExpression<T> Set<TValue>(Expression<Func<T, TValue>> expression, TValue value)
    {
        return set(toPath(expression), value);
    }

    public IPatchExpression<T> Duplicate<TElement>(Expression<Func<T, TElement>> expression, params Expression<Func<T, TElement>>[] destinations)
    {
        if (destinations.Length == 0)
            throw new ArgumentException("At least one destination must be given");

        var patch = new Dictionary<string, object>();
        patch.Add("type", "duplicate");
        patch.Add("path", toPath(expression));
        patch.Add("targets", destinations.Select(toPath).ToArray());
        _patchSet.Add(new PatchData(Items: patch, false));
        return this;
    }

    public IPatchExpression<T> Increment(Expression<Func<T, int>> expression, int increment = 1)
    {
        var patch = new Dictionary<string, object>();
        patch.Add("type", "increment");
        patch.Add("increment", increment);
        patch.Add("path", toPath(expression));
        _patchSet.Add(new PatchData(Items: patch, false));
        return this;
    }

    public IPatchExpression<T> Increment(Expression<Func<T, long>> expression, long increment = 1)
    {
        var patch = new Dictionary<string, object>();
        patch.Add("type", "increment");
        patch.Add("increment", increment);
        patch.Add("path", toPath(expression));
        _patchSet.Add(new PatchData(Items: patch, false));
        return this;
    }

    public IPatchExpression<T> Increment(Expression<Func<T, double>> expression, double increment = 1)
    {
        var patch = new Dictionary<string, object>();
        patch.Add("type", "increment_float");
        patch.Add("increment", increment);
        patch.Add("path", toPath(expression));
        _patchSet.Add(new PatchData(Items: patch, false));
        return this;
    }

    public IPatchExpression<T> Increment(Expression<Func<T, float>> expression, float increment = 1)
    {
        var patch = new Dictionary<string, object>();
        patch.Add("type", "increment_float");
        patch.Add("increment", increment);
        patch.Add("path", toPath(expression));
        _patchSet.Add(new PatchData(Items: patch, false));
        return this;
    }

    public IPatchExpression<T> Increment(Expression<Func<T, decimal>> expression, decimal increment = 1)
    {
        var patch = new Dictionary<string, object>();
        patch.Add("type", "increment_float");
        patch.Add("increment", increment);
        patch.Add("path", toPath(expression));
        _patchSet.Add(new PatchData(Items: patch, false));
        return this;
    }

    //TODO NRT - Annotations are currently inaccurate here due to lack of null guards. Replace with guards in .NET 6+
    public IPatchExpression<T> Append<TElement>(Expression<Func<T, IEnumerable<TElement>>> expression, TElement element)
    {
        var patch = new Dictionary<string, object>();
        patch.Add("type", "append");
        patch.Add("value", element);
        patch.Add("path", toPath(expression));

        var possiblyPolymorphic = element!.GetType() != typeof(TElement);
        _patchSet.Add(new PatchData(Items: patch, possiblyPolymorphic));
        return this;
    }

    public IPatchExpression<T> AppendIfNotExists<TElement>(Expression<Func<T, IEnumerable<TElement>>> expression, TElement element)
    {
        var patch = new Dictionary<string, object>();
        patch.Add("type", "append_if_not_exists");
        patch.Add("value", element);
        patch.Add("path", toPath(expression));

        var possiblyPolymorphic = element!.GetType() != typeof(TElement);
        _patchSet.Add(new PatchData(Items: patch, possiblyPolymorphic));

        return this;
    }

    public IPatchExpression<T> Insert<TElement>(Expression<Func<T, IEnumerable<TElement>>> expression, TElement element, int? index = null)
    {
        var patch = new Dictionary<string, object>();
        patch.Add("type", "insert");
        patch.Add("value", element);
        patch.Add("path", toPath(expression));
        if (index.HasValue)
        {
            patch.Add("index", index);
        }

        var possiblyPolymorphic = element!.GetType() != typeof(TElement);
        _patchSet.Add(new PatchData(Items: patch, possiblyPolymorphic));

        return this;
    }

    public IPatchExpression<T> InsertIfNotExists<TElement>(Expression<Func<T, IEnumerable<TElement>>> expression, TElement element, int? index = null)
    {
        var patch = new Dictionary<string, object>();
        patch.Add("type", "insert_if_not_exists");
        patch.Add("value", element);
        patch.Add("path", toPath(expression));
        if (index.HasValue)
        {
            patch.Add("index", index);
        }

        var possiblyPolymorphic = element!.GetType() != typeof(TElement);
        _patchSet.Add(new PatchData(Items: patch, possiblyPolymorphic));
        return this;
    }

    public IPatchExpression<T> Remove<TElement>(Expression<Func<T, IEnumerable<TElement>>> expression, TElement element, RemoveAction action = RemoveAction.RemoveFirst)
    {
        var patch = new Dictionary<string, object>();
        patch.Add("type", "remove");
        patch.Add("value", element);
        patch.Add("path", toPath(expression));
        patch.Add("action", (int)action);

        var possiblyPolymorphic = element!.GetType() != typeof(TElement);
        _patchSet.Add(new PatchData(Items: patch, possiblyPolymorphic));
        return this;
    }

    public IPatchExpression<T> Rename(string oldName, Expression<Func<T, object>> expression)
    {
        var patch = new Dictionary<string, object>();
        patch.Add("type", "rename");

        var newPath = toPath(expression);
        var parts = newPath.Split('.');

        var to = parts.Last();
        parts[parts.Length - 1] = oldName;

        var path = parts.Join(".");

        patch.Add("to", to);
        patch.Add("path", path);
        _patchSet.Add(new PatchData(Items: patch, false));

        return this;
    }

    public IPatchExpression<T> Delete(string name)
    {
        return delete(name);
    }

    public IPatchExpression<T> Delete<TParent>(string name, Expression<Func<T, TParent>> expression)
    {
        return delete(toPath(expression) + $".{name}");
    }

    public IPatchExpression<T> Delete<TElement>(Expression<Func<T, TElement>> expression)
    {
        return delete(toPath(expression));
    }

    private IPatchExpression<T> set<TValue>(string path, TValue value)
    {
        var patch = new Dictionary<string, object>();
        patch.Add("type", "set");
        patch.Add("value", value);
        patch.Add("path", path);
        _patchSet.Add(new PatchData(Items: patch, false));
        return this;
    }

    private IPatchExpression<T> delete(string path)
    {
        var patch = new Dictionary<string, object>();
        patch.Add("path", path);
        patch.Add("type", "delete");
        _patchSet.Add(new PatchData(Items: patch, false));
        return this;
    }

    private string toPath(Expression expression)
    {
        var visitor = new MemberFinder();
        visitor.Visit(expression);

        // TODO -- don't like this. Smells like duplication in logic
        return visitor.Members.Select(x => x.Name.FormatCase(_session.Serializer.Casing)).Join(".");
    }

    private DbObjectName PatchFunction => new PostgresqlObjectName(_session.Options.DatabaseSchemaName, "mt_jsonb_patch");
}
