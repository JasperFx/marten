using System;
using System.Linq;
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
    public async Task store_a_document_smoke_test()
    {
        var duplicatedDoc = new DuplicateValueTypeDoc(){DuplicateValueType = DuplicateValueType.From(Guid.NewGuid())};
        theSession.Store(duplicatedDoc);

        await theSession.SaveChangesAsync();

        (await theSession.Query<DuplicateValueTypeDoc>().AnyAsync()).ShouldBeTrue();
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

    [Fact]
    public async Task load_many()
    {
        var duplicatedDoc1 = new DuplicateValueTypeDoc{DuplicateValueType = DuplicateValueType.From(Guid.NewGuid())};
        var duplicatedDoc2 = new DuplicateValueTypeDoc{DuplicateValueType = DuplicateValueType.From(Guid.NewGuid())};
        var duplicatedDoc3 = new DuplicateValueTypeDoc{DuplicateValueType = DuplicateValueType.From(Guid.NewGuid())};
        theSession.Store(duplicatedDoc1, duplicatedDoc2, duplicatedDoc3);

        await theSession.SaveChangesAsync();

        var results = await theSession.Query<DuplicateValueTypeDoc>().Where(x => x.Id.IsOneOf(duplicatedDoc1.Id, duplicatedDoc2.Id, duplicatedDoc3.Id)).ToListAsync();
        results.Count.ShouldBe(3);
    }

    [Fact]
    public async Task use_in_LINQ_where_clause()
    {
        var duplicatedDoc = new DuplicateValueTypeDoc{DuplicateValueType = DuplicateValueType.From(Guid.NewGuid())};
        theSession.Store(duplicatedDoc);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<DuplicateValueTypeDoc>()
            .FirstOrDefaultAsync(x => x.DuplicateValueType == duplicatedDoc.DuplicateValueType);

        loaded.ShouldNotBeNull();
    }

    [Fact]
    public async Task use_in_LINQ_duplicatedDoc_clause()
    {
        var duplicatedDoc = new DuplicateValueTypeDoc{DuplicateValueType = DuplicateValueType.From(Guid.NewGuid())};
        theSession.Store(duplicatedDoc);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<DuplicateValueTypeDoc>().OrderBy(x => x.Id).Take(3).ToListAsync();
    }

    [Fact]
    public async Task use_in_LINQ_select_clause()
    {
        var duplicatedDoc = new DuplicateValueTypeDoc{DuplicateValueType = DuplicateValueType.From(Guid.NewGuid())};
        theSession.Store(duplicatedDoc);

        await theSession.SaveChangesAsync();

        var loaded = await theSession.Query<DuplicateValueTypeDoc>().Select(x => x.Id).Take(3).ToListAsync();

    }
}

[ValueObject<Guid>]
public readonly partial struct DuplicateValueType;

public class DuplicateValueTypeDoc
{
    public Guid Id { get; set; }
    public DuplicateValueType DuplicateValueType { get; set; }
}
