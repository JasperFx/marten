using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using LamarCodeGeneration;
using LamarCodeGeneration.Frames;
using Marten.Schema.Identity;
using Marten.Storage;
using Shouldly;
using Xunit;

namespace Marten.Schema.Testing.Identity.Sequences
{
    #region sample_custom-id-generation
    public class CustomdIdGeneration : IIdGeneration
    {
        public IEnumerable<Type> KeyTypes { get; } = new Type[] {typeof(string)};

        public bool RequiresSequences { get; } = false;
        public void GenerateCode(GeneratedMethod assign, DocumentMapping mapping)
        {
            var document = new Use(mapping.DocumentType);
            assign.Frames.Code($"_setter({{0}}, \"newId\");", document);
            assign.Frames.Code($"return {{0}}.{mapping.IdMember.Name};", document);
        }

    }
    #endregion sample_custom-id-generation

    public class CustomKeyGenerationTests : IntegrationContext
    {
        [Fact]
        public void When_a_custom_id_generation_is_used()
        {
            StoreOptions(options =>
            {
                #region sample_configuring-global-custom
                options.Policies.ForAllDocuments(m =>
                {
                    if (m.IdType == typeof(Guid))
                    {
                        m.IdStrategy = new CustomdIdGeneration();
                    }
                });
                #endregion sample_configuring-global-custom
            });

            #region sample_configuring-global-custom-test
            using (var session = theStore.OpenSession())
            {
                session.Store(new UserWithString { LastName = "last" });
                session.SaveChanges();
            }

            using (var session1 = theStore.QuerySession())
            {
                var users = session1.Query<UserWithString>().ToArray<UserWithString>();
                users.Single(user => user.LastName == "last").Id.ShouldBe("newId");
            }
            #endregion sample_configuring-global-custom-test
        }

        [Fact]
        public void When_a_custom_stregy_is_defined_for_a_single_document_then_Guid_should_be_used_as_Default()
        {
            StoreOptions(options =>
            {
                #region sample_configuring-mapping-specific-custom
                options.Schema.For<UserWithString>().IdStrategy(new CustomdIdGeneration());
                #endregion sample_configuring-mapping-specific-custom
            });

            theStore.Storage.MappingFor(typeof(UserWithString)).As<DocumentMapping>().IdStrategy.ShouldBeOfType<CustomdIdGeneration>();
        }
    }
}
