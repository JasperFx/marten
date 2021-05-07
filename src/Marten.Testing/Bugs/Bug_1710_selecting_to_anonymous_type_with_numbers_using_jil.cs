using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.Events.Projections;
using Marten.Schema;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1710_selecting_to_anonymous_type_with_numbers_using_jil : BugIntegrationContext
    {
        [Fact]
        public async Task select_to_anonymous_type()
        {
            StoreOptions(opts =>
            {
                opts.Serializer(new JilSerializer());
            });

            var myDoc = new MyDoc
            {
                Name = "foo",
                Number = 16
            };

            theSession.Store(myDoc);
            await theSession.SaveChangesAsync();



            var custom = await theSession.Query<MyDoc>().Where(x => x.Id == myDoc.Id)
                .Select(x => new {Name = x.Name, Number = x.Number}).SingleAsync();

            custom.Number.ShouldBe(myDoc.Number);
        }

        [DocumentAlias("mydoc")]
        public class MyDoc
        {
            public Guid Id { get; set; }
            public string Name { get; set; }
            public int Number { get; set; }
        }
    }
}
