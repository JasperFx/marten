using System;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Marten.Transforms;
using Marten.Util;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.Testing.Transforms
{
    public class TransformFunctionTests : IntegrationContext
    {
        private readonly string _getFullnameJs = AppContext.BaseDirectory.AppendPath("get_fullname.js");

        [Fact]
        public void derive_function_name_from_logical_name()
        {
            var func = new TransformFunction(new StoreOptions(), "something",
                "module.exports = function(doc){return doc;};");


            func.Identifier.Name.ShouldBe("mt_transform_something");
        }

        [Fact]
        public void derive_function_with_periods_in_the_name()
        {
            var func = new TransformFunction(new StoreOptions(), "nfl.team.chiefs",
                "module.exports = function(doc){return doc;};");

            func.Identifier.Name.ShouldBe("mt_transform_nfl_team_chiefs");
        }

        [Fact]
        public void picks_up_the_schema_from_storeoptions()
        {
            var options = new StoreOptions
            {
                DatabaseSchemaName = "other"
            };

            var func = new TransformFunction(options, "nfl.team.chiefs",
                "module.exports = function(doc){return doc;};");


            func.Identifier.Schema.ShouldBe("other");

        }

        [Fact]
        public void create_function_for_file()
        {
            var options = new StoreOptions();
            var func = TransformFunction.ForFile(options, _getFullnameJs);

            func.Name.ShouldBe("get_fullname");

            SpecificationExtensions.ShouldContain(func.Body, "module.exports");

            func.Identifier.Name.ShouldBe("mt_transform_get_fullname");
        }

        [Fact]
        public void end_to_end_test_using_the_transform()
        {
            var user = new User {FirstName = "Jeremy", LastName = "Miller"};
            var json = new TestsSerializer().ToCleanJson(user);

            var func = TransformFunction.ForFile(new StoreOptions(), _getFullnameJs);

            using (var conn = theStore.Tenancy.Default.OpenConnection())
            {
                conn.Execute(cmd => cmd.Sql(func.GenerateFunction()).ExecuteNonQuery());

                var actual = conn.Execute(cmd =>
                {
                    return cmd.Sql("select mt_transform_get_fullname(:json)")
                        .WithJsonParameter("json", json).ExecuteScalar().As<string>();
                });

                actual.ShouldBe("{\"fullname\": \"Jeremy Miller\"}");
            }
        }


        public TransformFunctionTests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }


}
