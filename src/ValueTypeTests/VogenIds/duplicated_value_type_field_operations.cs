using System;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Vogen;

namespace ValueTypeTests.VogenIds;

public class duplicated_value_type_field_operations : IDisposable, IAsyncDisposable
{
    private readonly DocumentStore theStore;
    private IDocumentSession theSession;

    public duplicated_value_type_field_operations()
    {
        theStore = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "duplicated_value_type_field";

            opts.ApplicationAssembly = GetType().Assembly;
            opts.GeneratedCodeMode = TypeLoadMode.Auto;
            opts.GeneratedCodeOutputPath =
                AppContext.BaseDirectory.ParentDirectory().ParentDirectory().ParentDirectory().AppendPath("Internal", "Generated");

            opts.RegisterValueType<DuplicateValueType>();
            opts.Schema.For<DuplicateValueTypeDoc>().Duplicate(x => x.DuplicateValueType);
        });

        theSession = theStore.LightweightSession();
    }

    public void Dispose()
    {
        theStore?.Dispose();
        theSession?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (theStore != null)
        {
            await theStore.DisposeAsync();
        }
    }

    [Fact]
    public async Task load_document()
    {
        var duplicatedDoc = new DuplicateValueTypeDoc
        {
            Id = Guid.NewGuid(),
            DuplicateValueType = DuplicateValueType.From(Guid.NewGuid())
        };
        theSession.Store(duplicatedDoc);

        await theSession.SaveChangesAsync();

        (await theSession.LoadAsync<DuplicateValueTypeDoc>(duplicatedDoc.Id))
            .DuplicateValueType.ShouldBe(duplicatedDoc.DuplicateValueType);
    }
}

[ValueObject<Guid>]
public readonly partial struct DuplicateValueType;

public class DuplicateValueTypeDoc
{
    public Guid Id { get; set; }
    public DuplicateValueType DuplicateValueType { get; set; }
}
