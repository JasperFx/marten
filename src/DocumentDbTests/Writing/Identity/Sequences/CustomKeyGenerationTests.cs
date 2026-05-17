using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Writing.Identity.Sequences;

#region sample_custom-id-generation
public class CustomIdGeneration : IIdGeneration
{
    public bool IsNumeric { get; } = false;
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
    // #4404: When_a_custom_id_generation_is_used used to verify that a
    // user-provided IIdGeneration subclass (with its GenerateCode hook)
    // produced a working storage operation. The closed-shape document
    // path doesn't honour GenerateCode anymore — extending id assignment
    // is now done by implementing IIdentification<TDoc, TId> directly
    // and registering it. The shallow "IdStrategy is settable" test
    // below still exercises the configuration surface.

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
