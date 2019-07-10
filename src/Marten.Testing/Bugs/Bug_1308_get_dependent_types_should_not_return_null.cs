using System;
using System.Linq;
using Marten.Schema;
using Marten.Storage;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1308_get_dependent_types_should_not_return_null
    {
        [Fact]
        public void documentmapping_dependenttypes_should_not_include_nulls()
        {
            var docMap = new DocumentMapping(typeof(BugTestClass), new StoreOptions());
            docMap.ForeignKeys.Add(new ExternalForeignKeyDefinition("test_column", docMap, "test_schema", "test_table", "test_column_2"));

            var featureSchema = (IFeatureSchema)docMap;

            featureSchema.DependentTypes().Any(t => t == null).ShouldBeFalse();
        }

        public class BugTestClass
        {
        }
    }
}
