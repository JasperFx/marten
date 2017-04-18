using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baseline;
using Marten.Schema;
using Marten.Schema.Identity;
using Marten.Storage;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema.Identity.Sequences
{
    // SAMPLE: custom-id-generation
    public class CustomdIdGeneration : IIdGeneration
    {
        public IEnumerable<Type> KeyTypes { get; } = new Type[] {typeof(string)};

        public IIdGenerator<T> Build<T>(ITenant tenant)
        {
            return (IIdGenerator<T>) new CustomIdGenerator();
        }

        public class CustomIdGenerator : IIdGenerator<string>
        {
            public string Assign(string existing, out bool assigned)
            {
                assigned = true;
                return "newId";
            }
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
                var store = container.GetInstance<IDocumentStore>().As<DocumentStore>();
                store.Storage.MappingFor(typeof(UserWithString)).As<DocumentMapping>().IdStrategy.ShouldBeOfType<CustomdIdGeneration>();
            }
        }
    }
}