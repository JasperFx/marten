using System;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Testing.Harness;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1841_float_as_duplicated_field : BugIntegrationContext
    {
        [Fact]
        public async Task let_it_work()
        {
            var doc = new FloatValueDoc {Value = 123.45f};
            theSession.Store(doc);

            await theSession.SaveChangesAsync();


        }
    }

    public class FloatValueDoc
    {
        public Guid Id { get; set; }

        [DuplicateField]
        public float Value { get; set; }

    }
}
