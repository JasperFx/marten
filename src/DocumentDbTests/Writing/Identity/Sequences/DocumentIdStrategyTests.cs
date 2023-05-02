using System;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using CombGuidIdGeneration = Marten.Schema.Identity.CombGuidIdGeneration;

namespace DocumentDbTests.Writing.Identity.Sequences;

public class DocumentIdStrategyTests: OneOffConfigurationsContext
{
    [Fact]
    public void uses_no_id_generation_for_non_public_id()
    {
        theStore.StorageFeatures.MappingFor(typeof(DocumentWithNonPublicId)).As<DocumentMapping>().IdStrategy
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
        theStore.StorageFeatures.MappingFor(typeof(DocumentWithoutIdSetter)).As<DocumentMapping>().IdStrategy
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
