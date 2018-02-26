using System;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Schema;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Transforms;
using Marten.Util;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marten.Testing.Transforms
{
    public class TransformFunctionTests
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

            func.Body.ShouldContain("module.exports");

            func.Identifier.Name.ShouldBe("mt_transform_get_fullname");
        }



        [Fact]
        public void generates_execute_transform_function_file()
        {
            var func = TransformFunction.ForFile(new StoreOptions(), _getFullnameJs);
            var tableName = "mt_test_table";
            var patch = new SchemaPatch(new DdlRules());

            var directory = AppContext.BaseDirectory.AppendPath("bin", "transforms");

            var fileSystem = new FileSystem();
            fileSystem.DeleteDirectory(directory);
            fileSystem.CreateDirectory(directory);

            var fileName = Path.GetFileNameWithoutExtension(_getFullnameJs);

            var filePath = directory.AppendPath($"execute_transform_{fileName}.sql");

            func.WritePatchForAllDocuments(patch, tableName, filePath);

            fileSystem.FileExists(directory.AppendPath($"execute_transform_{fileName}.sql"));
        }

        [Fact]
        public void generated_script_includes_function_body()
        {
            var func = getTransformationFunction();
            var script = func.GenerateTransformExecutionScript("test.test_table");

            script.ShouldContain(func.GenerateFunction());
        }

        [Fact]
        public void generated_script_includes_execution_function()
        {
            var func = getTransformationFunction();
            var script = func.GenerateTransformExecutionScript("test.test_table");
            var functionName = $"test.execute_transform_{func.Function.Name}";

            script.ShouldContain($"CREATE OR REPLACE FUNCTION {functionName}");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void generated_script_performs_invocation_when_prompted_to(bool shouldImmediatelyInvoke)
        {
            var func = getTransformationFunction();
            var script = func.GenerateTransformExecutionScript("test.test_table", shouldImmediatelyInvoke);
            var functionName = $"test.execute_transform_{func.Function.Name}";

            script.Contains($"PERFORM {functionName}").ShouldBe(shouldImmediatelyInvoke);
        }

        private TransformFunction getTransformationFunction()
        {
            return TransformFunction.ForFile(new StoreOptions
            {
                DatabaseSchemaName = "test"
            }, _getFullnameJs);
        }

        [Fact]
        public void end_to_end_test_using_the_transform()
        {
            using (var store = TestingDocumentStore.Basic())
            {
                var user = new User {FirstName = "Jeremy", LastName = "Miller"};
                var json = new TestsSerializer().ToCleanJson(user);

                var func = TransformFunction.ForFile(new StoreOptions(), _getFullnameJs);

                using (var conn = store.Tenancy.Default.OpenConnection())
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
        }


    }


}