using System;
using System.Collections.Generic;
using System.Linq;
using JasperFx.Core;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.Core.Reflection;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Writing.Identity.Sequences;

#region sample_custom-id-generation
public class CustomIdGeneration : IIdGeneration
{
    public IEnumerable<Type> KeyTypes { get; } = new Type[] {typeof(string)};

    public bool RequiresSequences { get; } = false;
    public void GenerateCode(GeneratedMethod assign, DocumentMapping mapping)
    {
        var document = new Use(mapping.DocumentType);
        assign.Frames.Code($"_setter({{0}}, \"newId\");", document);
        assign.Frames.Code($"return {{0}}.{mapping.CodeGen.AccessId};", document);
    }

}
#endregion

public class CustomKeyGenerationTests : OneOffConfigurationsContext
{
    [Fact]
    public void When_a_custom_id_generation_is_used()
    {
        StoreOptions(options =>
        {
            #region sample_configuring-global-custom
            options.Policies.ForAllDocuments(m =>
            {
                if (m.IdType == typeof(string))
                {
                    m.IdStrategy = new CustomIdGeneration();
                }
            });
            #endregion
        });

        #region sample_configuring-global-custom-test
        using (var session = theStore.LightweightSession())
        {
            session.Store(new UserWithString { LastName = "last" });
            session.SaveChanges();
        }

        using (var session1 = theStore.QuerySession())
        {
            var users = session1.Query<UserWithString>().ToArray();
            users.Single(user => user.LastName == "last").Id.ShouldBe("newId");
        }
        #endregion
    }

    [Fact]
    public void When_a_custom_stregy_is_defined_for_a_single_document_then_Guid_should_be_used_as_Default()
    {
        StoreOptions(options =>
        {
            #region sample_configuring-mapping-specific-custom
            options.Schema.For<UserWithString>().IdStrategy(new CustomIdGeneration());
            #endregion
        });

        theStore.StorageFeatures.MappingFor(typeof(UserWithString)).As<DocumentMapping>().IdStrategy.ShouldBeOfType<CustomIdGeneration>();
    }
}
