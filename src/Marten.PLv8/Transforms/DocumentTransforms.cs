using System;
using System.Linq.Expressions;
using Marten.Internal.Sessions;
using Marten.Linq.SqlGeneration;
using Marten.Linq.SqlGeneration.Filters;
using Marten.Services;
using Marten.Storage;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.PLv8.Transforms;

internal class DocumentTransforms: IDocumentTransforms, IDisposable
{
    private readonly bool _ownsSession;

    public DocumentTransforms(DocumentStore store, Tenant tenant)
    {
        Session = (DocumentSessionBase)store.LightweightSession(new SessionOptions { Tenant = tenant });
        _ownsSession = true;
    }

    public DocumentTransforms(DocumentSessionBase session)
    {
        Session = session;
    }

    public void Dispose()
    {
        if (_ownsSession)
        {
            Session?.Dispose();
        }
    }

    public DocumentSessionBase Session { get; }

    public void All<T>(string transformName)
    {
        var transform = Session.Options.TransformFor(transformName);
        var storage = Session.Options.Providers.StorageFor<T>().QueryOnly;

        var operation = new DocumentTransformOperationFragment(storage, transform);
        var statement = new StatementOperation(storage, operation);

        // To bake in the default document filtering here

        throw new NotImplementedException("Come back here.");
        //statement.CompileLocal(Session);
        Session.QueueOperation(statement);
    }

    public void Tenant<T>(string transformName, string tenantId)
    {
        Where<T>(transformName, x => x.TenantIsOneOf(tenantId));
    }

    public void Tenants<T>(string transformName, params string[] tenantIds)
    {
        Where<T>(transformName, x => x.TenantIsOneOf(tenantIds));
    }

    public void Where<T>(string transformName, Expression<Func<T, bool>> @where)
    {
        var transform = Session.Options.TransformFor(transformName);

        var storage = Session.StorageFor<T>();
        var operation = new DocumentTransformOperationFragment(storage, transform);

        var statement = new StatementOperation(storage, operation);
        statement.ApplyFiltering(Session, @where);
        Session.QueueOperation(statement);
    }


    public void Document<T>(string transformName, string id)
    {
        transformOne<T>(transformName, new ByStringFilter(id));
    }

    private void transformOne<T>(string transformName, ISqlFragment filter)
    {
        var transform = Session.Options.TransformFor(transformName);

        var storage = Session.StorageFor<T>();
        var operation = new DocumentTransformOperationFragment(storage, transform);

        var statement = new StatementOperation(storage, operation, filter);
        Session.QueueOperation(statement);
    }

    public void Document<T>(string transformName, int id)
    {
        transformOne<T>(transformName, new ByIntFilter(id));
    }

    public void Document<T>(string transformName, long id)
    {
        transformOne<T>(transformName, new ByLongFilter(id));
    }

    public void Document<T>(string transformName, Guid id)
    {
        transformOne<T>(transformName, new ByGuidFilter(id));
    }
}
