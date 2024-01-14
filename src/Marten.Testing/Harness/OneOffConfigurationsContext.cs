using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten.Internal.CodeGeneration;
using Marten.TestHelpers;
using Xunit;

namespace Marten.Testing.Harness;

#region sample_one_off_test_context
[Collection("OneOffs")]
public abstract class OneOffConfigurationsContext : IDisposable
{
    private readonly OneOffConfigurationsHelper _helper;
    public string SchemaName { get; protected set; }

    protected OneOffConfigurationsContext()
    {
        SchemaName = GetType().Name.ToLower().Sanitize();
        _helper = new OneOffConfigurationsHelper(SchemaName, ConnectionSource.ConnectionString);
    }

    protected OneOffConfigurationsContext(OneOffConfigurationsHelper oneOffConfigurationsHelper)
    {
        SchemaName = GetType().Name.ToLower().Sanitize();
        _helper = oneOffConfigurationsHelper;
    }
    
    public DocumentStore SeparateStore(Action<StoreOptions> configure = null) => _helper.SeparateStore(configure);

    public DocumentStore StoreOptions(Action<StoreOptions> configure, bool cleanAll = true) => _helper.StoreOptions(configure, cleanAll);

    public DocumentStore TheStore => _helper.TheStore;

    public IDocumentSession TheSession => _helper.TheSession;
    public IList<IDisposable> Disposables => _helper.Disposables;

    public Task AppendEvent(Guid streamId, params object[] events) => _helper.AppendEvent(streamId, events);
    public void Dispose() => _helper.Dispose();
}
#endregion
