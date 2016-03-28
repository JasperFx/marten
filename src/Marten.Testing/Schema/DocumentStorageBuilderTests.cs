using System;
using System.Diagnostics;
using System.Linq;
using Marten.Schema;
using Marten.Testing.Documents;
using NpgsqlTypes;
using Shouldly;
using StructureMap;
using Xunit;

namespace Marten.Testing.Schema
{
    public class DocumentStorageBuilderTests
    {
        [Fact]
        public void do_not_blow_up_building_one()
        {
            var mapping = DocumentMapping.For<User>();

            var storage = DocumentStorageBuilder.Build(null, mapping);

            storage.ShouldNotBeNull();


            storage.IdType.ShouldBe(NpgsqlDbType.Uuid);

            
        }

        [Fact]
        public void implements_the_id_type()
        {
            DocumentStorageBuilder.Build(null, DocumentMapping.For<User>()).IdType.ShouldBe(NpgsqlDbType.Uuid);

            var schema = Container.For<DevelopmentModeRegistry>().GetInstance<IDocumentSchema>();

            DocumentStorageBuilder.Build(schema, typeof (IntDoc)).IdType.ShouldBe(NpgsqlDbType.Integer);
            DocumentStorageBuilder.Build(schema, typeof (LongDoc)).IdType.ShouldBe(NpgsqlDbType.Bigint);
            DocumentStorageBuilder.Build(null, typeof (StringDoc)).IdType.ShouldBe(NpgsqlDbType.Text);
        }


        [Fact]
        public void implements_the_identity_method()
        {
            var schema = Container.For<DevelopmentModeRegistry>().GetInstance<IDocumentSchema>();

            var guid = Guid.NewGuid();

            DocumentStorageBuilder.Build(schema, typeof (IntDoc)).Identity(new IntDoc {Id = 3}).ShouldBe(3);
            DocumentStorageBuilder.Build(schema, typeof (LongDoc)).Identity(new LongDoc {Id = 4}).ShouldBe(4L);
            DocumentStorageBuilder.Build(null, typeof (StringDoc)).Identity(new StringDoc {Id = "abc"}).ShouldBe("abc");
            DocumentStorageBuilder.Build(null, typeof (User)).Identity(new User {Id = guid}).ShouldBe(guid);
        }

        [Fact]
        public void do_not_blow_up_building_more_than_one()
        {
            var mappings = new[]
            {
                DocumentMapping.For<User>(),
                DocumentMapping.For<Company>(),
                DocumentMapping.For<Issue>()
            };

            DocumentStorageBuilder.Build(null, mappings).Count()
                .ShouldBe(3);
        }

    }


    public class StringDoc
    {
        public string Id;
    }
}