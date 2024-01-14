using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Internal.CodeGeneration;
using Marten.TestHelpers;
using Xunit;

namespace Marten.Testing.Harness;

[Collection("OneOffs")]
public abstract class OneOffConfigurationsContext : IDisposable
{
    protected readonly OneOffConfigurationsHelper Helper;
    public string SchemaName { get; protected set; }

    protected OneOffConfigurationsContext()
    {
        SchemaName = GetType().Name.ToLower().Sanitize();
        Helper = new OneOffConfigurationsHelper(SchemaName, ConnectionSource.ConnectionString);
    }
    
    public DocumentStore SeparateStore(Action<StoreOptions> configure = null) => Helper.SeparateStore(configure);

    public DocumentStore StoreOptions(Action<StoreOptions> configure, bool cleanAll = true) => Helper.StoreOptions(configure, cleanAll);

    public DocumentStore TheStore => Helper.TheStore;

    public IDocumentSession TheSession => Helper.TheSession;
    public IList<IDisposable> Disposables => Helper.Disposables;

    public Task AppendEvent(Guid streamId, params object[] events) => Helper.AppendEvent(streamId, events);
    public void Dispose() => Helper.Dispose();
}
