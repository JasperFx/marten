using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.CodeGeneration;
using JasperFx.Core;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Vogen;
using Xunit.Abstractions;

namespace ValueTypeTests;

public class include_usage : IAsyncDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly DocumentStore theStore;
    private IDocumentSession theSession;

    public include_usage(ITestOutputHelper output)
    {
        _output = output;

        theStore = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);
            opts.DatabaseSchemaName = "strong_typed";

            opts.ApplicationAssembly = GetType().Assembly;
            opts.GeneratedCodeMode = TypeLoadMode.Auto;
            opts.GeneratedCodeOutputPath =
                AppContext.BaseDirectory.ParentDirectory().ParentDirectory().ParentDirectory().AppendPath("Internal", "Generated");
        });

        theSession = theStore.LightweightSession();
    }

    public async ValueTask DisposeAsync()
    {
        if (theStore != null)
        {
            await theStore.DisposeAsync();
        }
    }

    [Fact]
    public async Task include_a_single_reference()
    {
        var teacher = new Teacher();
        var c = new Class();

        theSession.Store(teacher);

        c.TeacherId = teacher.Id;
        theSession.Store(c);

        await theSession.SaveChangesAsync();

        theSession.Logger = new TestOutputMartenLogger(_output);

        var list = new List<Teacher>();

        var loaded = await theSession
            .Query<Class>()
            .Include<Teacher>(c => c.TeacherId, list)
            .Where(x => x.Id == c.Id)
            .FirstOrDefaultAsync();

        loaded.Id.ShouldBe(c.Id);
        list.Single().Id.ShouldBe(teacher.Id);
    }

    [Fact]
    public async Task include_multiple_references()
    {
        var teacher1 = new Teacher();
        var teacher2 = new Teacher();
        var teacher3 = new Teacher();
        var teacher4 = new Teacher();

        theSession.Store(teacher1, teacher2, teacher3, teacher4);

        var grade = new Grade();
        grade.Teachers.Add(teacher1.Id.Value);
        grade.Teachers.Add(teacher2.Id.Value);
        grade.Teachers.Add(teacher3.Id.Value);

        await theSession.SaveChangesAsync();

        var list = new List<Teacher>();

        var loaded = await theSession
            .Query<Grade>()
            .Include<Teacher>(c => c.Teachers, list)
            .Where(x => x.Id == grade.Id)
            .FirstOrDefaultAsync();

        loaded.Id.ShouldBe(grade.Id);
        list.Count.ShouldBe(3);
    }
}

[ValueObject<Guid>]
public partial struct TeacherId;

public class Teacher
{
    public TeacherId? Id { get; set; }
}

[ValueObject<Guid>]
public partial struct ClassId;

public class Class
{
    public ClassId? Id { get; set; }
    public TeacherId? TeacherId { get; set; }
}

[ValueObject<Guid>]
public partial struct GradeId;

public class Grade
{
    public GradeId? Id { get; set; }
    public List<TeacherId>? Teachers { get; set; } = new();
}



