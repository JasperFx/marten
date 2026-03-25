#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events;
using Marten.Events.Operations;
using Marten.Events.Protected;
using Marten.Internal.Sessions;
using Marten.Linq.Parsing;
using Marten.Storage;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Events;

internal partial class EventStore: QueryEventStore, IEventStoreOperations
{
    private readonly DocumentSessionBase _session;
    private readonly DocumentStore _store;

    public EventStore(DocumentSessionBase session, DocumentStore store, Tenant tenant): base(session, store, tenant)
    {
        _session = session;
        _store = store;
    }

    public IEvent BuildEvent(object data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));
        return _store.Events.BuildEvent(data);
    }

    public void OverwriteEvent(IEvent e)
    {
        var op = new OverwriteEventOperation(_store.Events, e);
        _session.QueueOperation(op);
    }

    public void AssignTagWhere(Expression<Func<IEvent, bool>> expression, object tag)
    {
        if (expression == null) throw new ArgumentNullException(nameof(expression));
        if (tag == null) throw new ArgumentNullException(nameof(tag));

        var tagType = tag.GetType();
        var registration = _store.Events.FindTagType(tagType)
                           ?? throw new InvalidOperationException(
                               $"Tag type '{tagType.Name}' is not registered. Call RegisterTagType<{tagType.Name}>() first.");

        var value = registration.ExtractValue(tag);
        var schema = _store.Events.DatabaseSchemaName;

        // Parse the expression into a SQL WHERE fragment using EventQueryMapping
        var mapping = new EventQueryMapping(_store.Options);
        var holder = new SimpleWhereFragmentHolder();
        var parser = new WhereClauseParser(_store.Options, mapping.QueryMembers, holder);
        parser.Visit(expression.Body);

        ISqlFragment whereFragment = holder.Fragments.Count switch
        {
            0 => throw new ArgumentException("Expression did not produce any WHERE clause."),
            1 => holder.Fragments[0],
            _ => CompoundWhereFragment.And(holder.Fragments)
        };

        var isConjoined = _store.Events.TenancyStyle == Storage.TenancyStyle.Conjoined;
        var op = new AssignTagWhereOperation(schema, registration, value, whereFragment, isConjoined);
        _session.QueueOperation(op);
    }

    private class SimpleWhereFragmentHolder: IWhereFragmentHolder
    {
        public List<ISqlFragment> Fragments { get; } = new();

        public void Register(ISqlFragment filter)
        {
            if (filter != null) Fragments.Add(filter);
        }
    }
}
