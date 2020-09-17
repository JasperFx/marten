using System;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Bugs
{
    [Collection("bug1429")]
    public class Bug_1429_fk_ordering_problems : OneOffConfigurationsContext
    {
        private readonly ITestOutputHelper _output;

        public Bug_1429_fk_ordering_problems(ITestOutputHelper output) : base("bug1429")
        {
            _output = output;
        }

        [Fact]
        public async Task try_to_persist()
        {
            StoreOptions(_ =>
            {
                _.Logger(new TestOutputMartenLogger(_output));
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Schema.For<DocB>()
                    .AddSubClassHierarchy(typeof(DocB1), typeof(DocB2))
                    .ForeignKey<DocA>(x => x.DocAId)
                    .ForeignKey<DocC>(x => x.DocCId);
            });


            using var session = theStore.LightweightSession();

            var doc_a = new DocA
            {
                Id = Guid.NewGuid()
            };

            var doc_c = new DocC
            {
                Id = Guid.NewGuid()
            };

            var docB1_1 = new DocB1
            {
                Id = Guid.NewGuid(),
                DocAId = doc_a.Id,
                DocCId = doc_c.Id,
                NameB1 =  "test"
            };

            var docB2_1 = new DocB2
            {
                Id = Guid.NewGuid(),
                DocAId = doc_a.Id,
                DocCId = doc_c.Id,
                NameB2 =  "test"
            };

            session.Store(doc_a);
            session.Store(docB1_1);
            session.Store(docB2_1);

            // We're proving that Marten can order the operations
            // here.
            session.Store(doc_c);
            await session.SaveChangesAsync();
        }

    }

    public class DocA
    {
        public Guid Id { get; set; }
    }

    public class DocB
    {
        public Guid Id { get; set; }
        public Guid DocAId { get; set; }
        public Guid DocCId { get; set; }
    }

    public class DocC
    {
        public Guid Id { get; set; }
    }

    public class DocB1 : DocB
    {
        public string NameB1 { get; set; }
    }

    public class DocB2 : DocB
    {
        public string NameB2 { get; set; }
    }

}
