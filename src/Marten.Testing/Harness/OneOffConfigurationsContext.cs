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
    protected readonly OneOffConfigurationsHelper OneOffConfigurationsHelper;
    public string SchemaName { get; }

    protected OneOffConfigurationsContext()
    {
        SchemaName = GetType().Name.ToLower().Sanitize();
        OneOffConfigurationsHelper = new OneOffConfigurationsHelper(SchemaName, ConnectionSource.ConnectionString);
    }

    protected OneOffConfigurationsContext(string schemaName)
    {
        SchemaName = schemaName;
        OneOffConfigurationsHelper = new OneOffConfigurationsHelper(SchemaName, ConnectionSource.ConnectionString);
    }

    protected OneOffConfigurationsContext(OneOffConfigurationsHelper oneOffConfigurationsOneOffConfigurationsHelper)
    {
        SchemaName = GetType().Name.ToLower().Sanitize();
        OneOffConfigurationsHelper = oneOffConfigurationsOneOffConfigurationsHelper;
    }
    
    public DocumentStore SeparateStore(Action<StoreOptions> configure = null) => OneOffConfigurationsHelper.SeparateStore(configure);

    public DocumentStore StoreOptions(Action<StoreOptions> configure, bool cleanAll = true) => OneOffConfigurationsHelper.StoreOptions(configure, cleanAll);

    public DocumentStore TheStore => OneOffConfigurationsHelper.TheStore;

    public IDocumentSession TheSession => OneOffConfigurationsHelper.TheSession;
    public IList<IDisposable> Disposables => OneOffConfigurationsHelper.Disposables;

    public Task AppendEvent(Guid streamId, params object[] events) => OneOffConfigurationsHelper.AppendEvent(streamId, events);
    public void Dispose() => OneOffConfigurationsHelper.Dispose();
}
#endregion
