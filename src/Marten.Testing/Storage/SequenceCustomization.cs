using System.Collections.Generic;
using System.Linq;
using Marten.Schema;
using Marten.Storage;
using Xunit;

namespace Marten.Testing.Storage
{
    public class SequenceCustomization : IntegratedFixture
    {
        public class SequenceWithStart : FeatureSchemaBase
        {
            private readonly string schema;

            public SequenceWithStart(StoreOptions options) : base(nameof(SequenceWithStart), options)
            {
                schema = options.DatabaseSchemaName;
            }

            protected override IEnumerable<ISchemaObject> schemaObjects()
            {
                yield return new Sequence(new DbObjectName(schema, $"mt_{nameof(SequenceWithStart).ToLowerInvariant()}"), 1337);
            }
        }

        [Fact]
        public void CanRegisterSequenceWithStartAttribute()
        {
            StoreOptions(_ =>
            {
                _.Storage.Add<SequenceWithStart>();
            });

            theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            using (var session = theStore.OpenSession())
            {
                var value = session.Query<int>("select nextval(?)",
                    $"{theStore.Options.DatabaseSchemaName}.mt_{nameof(SequenceWithStart).ToLowerInvariant()}").First();

                Assert.Equal(1337, value);
            }
        }
    }
}
