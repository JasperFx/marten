using System;
using System.IO;
using System.Linq;
using Baseline;
using Marten.Schema;
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


            func.Function.Name.ShouldBe("mt_transform_something");
        }

        [Fact]
        public void derive_function_with_periods_in_the_name()
        {
            var func = new TransformFunction(new StoreOptions(), "nfl.team.chiefs",
                "module.exports = function(doc){return doc;};");

            func.Function.Name.ShouldBe("mt_transform_nfl_team_chiefs");
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


            func.Function.Schema.ShouldBe("other");

        }

        [Fact]
        public void create_function_for_file()
        {
            var options = new StoreOptions();
            var func = TransformFunction.ForFile(options, _getFullnameJs);

            func.Name.ShouldBe("get_fullname");

            func.Body.ShouldContain("module.exports");

            func.Function.Name.ShouldBe("mt_transform_get_fullname");
        }

        [Fact]
        public void rebuilds_if_it_does_not_exist_in_the_schema_if_auto_create_is_all()
        {
            var schema = Substitute.For<IDocumentSchema>();
            schema.StoreOptions.Returns(new StoreOptions());

            var dbobjects = Substitute.For<IDbObjects>();
            schema.DbObjects.Returns(dbobjects);

            var func = TransformFunction.ForFile(new StoreOptions {AutoCreateSchemaObjects = AutoCreate.All}, _getFullnameJs);

            dbobjects.SchemaFunctionNames().Returns(Enumerable.Empty<FunctionName>());


            var patch = new SchemaPatch(new DdlRules());
            func.GenerateSchemaObjectsIfNecessary(AutoCreate.All, schema, patch);

            var generated = func.GenerateFunction();

            patch.UpdateDDL.ShouldContain(generated);
        }

        [Fact]
        public void rebuilds_if_it_does_not_exist_in_the_schema_if_auto_create_is_create_only()
        {
            var schema = Substitute.For<IDocumentSchema>();
            schema.StoreOptions.Returns(new StoreOptions());
            var dbobjects = Substitute.For<IDbObjects>();
            schema.DbObjects.Returns(dbobjects);

            var func = TransformFunction.ForFile(new StoreOptions { AutoCreateSchemaObjects = AutoCreate.CreateOnly }, _getFullnameJs);

            dbobjects.SchemaFunctionNames().Returns(Enumerable.Empty<FunctionName>());

            var patch = new SchemaPatch(new DdlRules());


            func.GenerateSchemaObjectsIfNecessary(AutoCreate.CreateOnly, schema, patch);

            var generated = func.GenerateFunction();

            patch.UpdateDDL.ShouldContain(generated);
        }

        [Fact]
        public void throws_exception_if_auto_create_is_none_and_the_function_does_not_exist()
        {
            var schema = Substitute.For<IDocumentSchema>();
            schema.StoreOptions.Returns(new StoreOptions());
            var dbobjects = Substitute.For<IDbObjects>();
            schema.DbObjects.Returns(dbobjects);

            var func = TransformFunction.ForFile(new StoreOptions { AutoCreateSchemaObjects = AutoCreate.None }, _getFullnameJs);

            dbobjects.SchemaFunctionNames().Returns(Enumerable.Empty<FunctionName>());

            var patch = new SchemaPatch(new DdlRules());


            Exception<InvalidOperationException>.ShouldBeThrownBy(() =>
            {
                func.GenerateSchemaObjectsIfNecessary(AutoCreate.None, schema, patch);
            });
        }

        [Fact]
        public void rebuilds_if_it_does_not_exist_in_the_schema_if_auto_create_is_create_or_update()
        {
            var schema = Substitute.For<IDocumentSchema>();
            schema.StoreOptions.Returns(new StoreOptions());

            var dbobjects = Substitute.For<IDbObjects>();
            schema.DbObjects.Returns(dbobjects);

            var func = TransformFunction.ForFile(new StoreOptions { AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate }, _getFullnameJs);

            dbobjects.SchemaFunctionNames().Returns(Enumerable.Empty<FunctionName>());

            var patch = new SchemaPatch(new DdlRules());


            func.GenerateSchemaObjectsIfNecessary(AutoCreate.CreateOrUpdate, schema, patch);

            var generated = func.GenerateFunction();
            
            patch.UpdateDDL.ShouldContain(generated);



            patch.RollbackDDL.ShouldContain("DROP FUNCTION IF EXISTS public.mt_transform_get_fullname(JSONB)");
        }

        [Fact]
        public void does_not_regenerate_the_function_if_it_exists()
        {
            var schema = Substitute.For<IDocumentSchema>();
            var dbobjects = Substitute.For<IDbObjects>();
            schema.DbObjects.Returns(dbobjects);

            var func = TransformFunction.ForFile(new StoreOptions(), _getFullnameJs);

            var body = new FunctionBody(func.Function, new string[0], func.GenerateFunction());

            dbobjects.DefinitionForFunction(func.Function).Returns(body);

            var patch = new SchemaPatch(new DdlRules());

            func.GenerateSchemaObjectsIfNecessary(AutoCreate.All, schema, patch);

            var generated = func.GenerateFunction();

            patch.UpdateDDL.ShouldNotContain(generated);
            patch.RollbackDDL.ShouldNotContain(func.Function.QualifiedName);
        }

        [Fact]
        public void end_to_end_test_using_the_transform()
        {
            using (var store = TestingDocumentStore.Basic())
            {
                var user = new User {FirstName = "Jeremy", LastName = "Miller"};
                var json = new TestsSerializer().ToCleanJson(user);

                var func = TransformFunction.ForFile(new StoreOptions(), _getFullnameJs);

                using (var conn = store.Advanced.OpenConnection())
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