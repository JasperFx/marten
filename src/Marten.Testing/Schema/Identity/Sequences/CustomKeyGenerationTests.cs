using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Marten.Schema;
using Marten.Schema.Identity;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema.Identity.Sequences
{
    // SAMPLE: custom-id-generation
    public class CustomdIdGeneration : IIdGeneration
    {
        public IEnumerable<StorageArgument> ToArguments()
        {
            return Enumerable.Empty<StorageArgument>();
        }

        public string AssignmentBodyCode(MemberInfo idMember)
        {
            return $@"
document.{idMember.Name} = ""newId""; //your id generation algorithm here
assigned = true;
";
        }

        public IEnumerable<Type> KeyTypes { get; }

        public IIdGeneration<T> Build<T>(IDocumentSchema schema)
        {
            throw new NotImplementedException();
        }
    }
    // ENDSAMPLE

    public class CustomKeyGenerationTests
    {
        [Fact]
        public void When_a_custom_id_generation_is_used()
        {
            using (var container = ContainerFactory.Configure(
                // SAMPLE: configuring-global-custom
                options => options.DefaultIdStrategy = (mapping, storeOptions) => new CustomdIdGeneration()))
                // ENDSAMPLE
            {
                container.GetInstance<DocumentCleaner>().CompletelyRemoveAll();

                var store = container.GetInstance<IDocumentStore>();

                // SAMPLE: configuring-global-custom-test
                using (var session = store.OpenSession())
                {
                    session.Store(new UserWithString { LastName = "last" });
                    session.SaveChanges();
                }

                using (var session1 = store.QuerySession())
                {
                    var users = session1.Query<UserWithString>().ToArray<UserWithString>();
                    users.Single(user => user.LastName == "last").Id.ShouldBe("newId");
                }
                // ENDSAMPLE
            }
        }

        [Fact]
        public void When_a_custom_stregy_is_defined_for_a_single_document_then_Guid_should_be_used_as_Default()
        {
            using (var container = ContainerFactory.Configure(
                // SAMPLE: configuring-mapping-specific-custom
                options => options.Schema.For<UserWithString>().IdStrategy(new CustomdIdGeneration())
                // ENDSAMPLE 
                        ))
            {
                var store = container.GetInstance<IDocumentStore>();
                store.Schema.MappingFor(typeof(UserWithString)).IdStrategy.ShouldBeOfType<CustomdIdGeneration>();
            }
        }
    }
}