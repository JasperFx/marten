using System;
using Baseline;
using Marten.Schema;
using Marten.Schema.Identity;
using Shouldly;
using StructureMap;
using Xunit;

namespace Marten.Testing.Schema.Identity.Sequences
{
    public class DocumentIdStrategyTests : IntegratedFixture
    {
        private readonly IContainer _container = Container.For<DevelopmentModeRegistry>();

        public DocumentIdStrategyTests()
        {
            _container.GetInstance<DocumentCleaner>().CompletelyRemoveAll();
        }

        [Fact]
        public void uses_no_id_generation_for_non_public_id()
        {
            var schema = _container.GetInstance<IDocumentSchema>();

            schema.MappingFor(typeof(DocumentWithNonPublicId)).As<DocumentMapping>().IdStrategy
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
            var schema = _container.GetInstance<IDocumentSchema>();
            schema.MappingFor(typeof(DocumentWithoutIdSetter)).As<DocumentMapping>().IdStrategy
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