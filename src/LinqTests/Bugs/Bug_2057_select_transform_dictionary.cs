using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx;
using Marten;
using Marten.Testing.Harness;
using Shouldly;

namespace LinqTests.Bugs;

public class Bug_2057_select_transform_dictionary: BugIntegrationContext
{
    [Fact]
    public async Task should_be_able_select_dictionary()
    {
        using var documentStore = SeparateStore(x =>
        {
            x.AutoCreateSchemaObjects = AutoCreate.All;
            x.Schema.For<TestEntity>();
        });

        await documentStore.Advanced.Clean.DeleteAllDocumentsAsync();

        await using var session = documentStore.LightweightSession();
        session.Store(new TestEntity
        {
            Name = "Test", Values = new Dictionary<string, string> { { "Key", "Value" }, { "Key2", "Value2" } }
        });

        await session.SaveChangesAsync();

        await using var querySession = documentStore.QuerySession();

        var results = await querySession.Query<TestEntity>()
            .Select(x => new TestDto { Name = x.Name, Values = x.Values })
            .ToListAsync();

        results.Count.ShouldBe(1);
        results[0].Name.ShouldBe("Test");
        results[0].Values.Keys.ShouldContain("Key");
        results[0].Values["Key"].ShouldBe("Value");
        results[0].Values.Keys.ShouldContain("Key2");
        results[0].Values["Key2"].ShouldBe("Value2");
    }
}

public class TestEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; }
    public Dictionary<string, string> Values { get; set; }
    public List<Guid> OtherIds { get; set; }
}

public class TestDto
{
    public string Name { get; set; }
    public Dictionary<string, string> Values { get; set; }
}
