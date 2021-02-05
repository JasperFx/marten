using System;
using Baseline;
using Marten.Schema.Identity;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing.Identity.Sequences
{
    public class DocumentIdStrategyTests : IntegrationContext
    {
        [Fact]
        public void uses_no_id_generation_for_non_public_id()
        {
            theStore.Storage.MappingFor(typeof(DocumentWithNonPublicId)).As<DocumentMapping>().IdStrategy
                .ShouldBeOfType<CombGuidIdGeneration>();
        }

        public class DocumentWithNonPublicId
        {
            public Guid Id { get; private set; }

            public string Name { get; set; }
        }

        [Fact]
        public void uses_no_id_generation_without_id_setter()
        {
            theStore.Storage.MappingFor(typeof(DocumentWithoutIdSetter)).As<DocumentMapping>().IdStrategy
                .ShouldBeOfType<NoOpIdGeneration>();
        }

        public class DocumentWithoutIdSetter
        {
            public DocumentWithoutIdSetter(Guid id)
            {
                Id = id;
            }

            public Guid Id { get; }

            public string Name { get; set; }
        }

    }
}
