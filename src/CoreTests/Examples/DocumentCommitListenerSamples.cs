using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using JasperFx.Events.Documents;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace CoreTests.Examples;

#region sample_writing_a_document_commit_listener

// The store-agnostic post-commit hook. The same class works unchanged against
// Marten, Polecat and Fisher -- nothing in it names a store.
public class AuditCommitListener: IDocumentCommitListener
{
    private readonly List<string> _audit = new();

    public Task AfterCommitAsync(
        IDocumentSessionOperations session,
        IDocumentChangeSet commit,
        CancellationToken token)
    {
        foreach (var document in commit.Inserted) _audit.Add($"Inserted {document.GetType().Name}");
        foreach (var document in commit.Updated) _audit.Add($"Updated {document.GetType().Name}");

        // Deletions arrive as descriptors rather than as documents, because
        // Delete<T>(id) and DeleteWhere<T>(...) never loaded one to report
        foreach (var deletion in commit.Deleted) _audit.Add($"Deleted {deletion.DocumentType.Name} {deletion.Id}");

        return Task.CompletedTask;
    }
}

#endregion

public interface IOrdersStore: IDocumentStore;

public static class DocumentCommitListenerSamples
{
    public static void register_by_container(IServiceCollection services, string connectionString)
    {
        #region sample_registering_a_document_commit_listener

        services.AddMarten(opts =>
        {
            opts.Connection(connectionString);

            // Directly, when you have the instance in hand
            opts.AddCommitListener(new AuditCommitListener());
        });

        // Or register it in the container, and AddMarten() will find it. Note
        // that this only reaches the MAIN store
        services.AddSingleton<IDocumentCommitListener, AuditCommitListener>();

        #endregion
    }

    public static void register_on_an_ancillary_store(IServiceCollection services, string connectionString)
    {
        #region sample_registering_a_document_commit_listener_on_ancillary_store

        services.AddMartenStore<IOrdersStore>(opts =>
        {
            opts.Connection(connectionString);
            opts.DatabaseSchemaName = "orders";
        });

        // Ancillary stores are NOT swept from the container -- otherwise every
        // listener registered anywhere would attach to every store -- so they
        // opt in explicitly
        services.ConfigureMarten<IOrdersStore>(opts => opts.AddCommitListener(new AuditCommitListener()));

        #endregion
    }
}
