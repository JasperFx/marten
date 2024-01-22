using System;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Testing.Harness;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_1841_float_as_duplicated_field : BugIntegrationContext
{
    [Fact]
    public async Task let_it_work()
    {
        var doc = new FloatValueDoc {Value = 123.45f};
        TheSession.Store(doc);

        await TheSession.SaveChangesAsync();


    }
}

public class FloatValueDoc
{
    public Guid Id { get; set; }

    [DuplicateField]
    public float Value { get; set; }

}
