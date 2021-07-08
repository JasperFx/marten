using System;
using System.Linq;
using Baseline;
using Marten.PLv8.Transforms;
using Marten.Services;
using Marten.Testing;
using Marten.Testing.Documents;
using Marten.Testing.Events;
using Marten.Testing.Harness;
using Npgsql;
using NpgsqlTypes;
using Shouldly;
using Weasel.Postgresql;
using Xunit;
using Issue = Marten.Testing.Documents.Issue;

namespace Marten.PLv8.Testing.Transforms
{
    [Collection("transforms")]
    public class TransformFunctionTests : OneOffConfigurationsContext
    {
        private readonly string _getFullnameJs = AppContext.BaseDirectory.AppendPath("get_fullname.js");

        private readonly string _binAllsql = AppContext.BaseDirectory.AppendPath("bin", "allsql");
        private readonly string _binAllsql2 = AppContext.BaseDirectory.AppendPath("bin", "allsql2");


        [Fact]
        public void writes_transform_function()
        {
            using (var store = DocumentStore.For(_ =>
            {
                _.RegisterDocumentType<User>();
                _.RegisterDocumentType<Company>();
                _.RegisterDocumentType<Issue>();

                _.Events.AddEventType(typeof(MembersJoined));

                _.Connection(ConnectionSource.ConnectionString);

                _.UseJavascriptTransformsAndPatching(x => x.LoadFile("get_fullname.js"));

            }))
            {
                store.Schema.WriteDatabaseCreationScriptByType(_binAllsql);
            }

            var file = _binAllsql.AppendPath("transforms.sql");
            var lines = new FileSystem().ReadStringFromFile(file).ReadLines().ToArray();


            lines.ShouldContain(
                "CREATE OR REPLACE FUNCTION public.mt_transform_get_fullname(doc JSONB) RETURNS JSONB AS $$");
        }


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
                var cmd = new NpgsqlCommand(func.GenerateFunction());
                conn.Execute(cmd);

                var cmd2 = new NpgsqlCommand("select mt_transform_get_fullname(:json)").With("json", json, NpgsqlDbType.Jsonb);
                var actual = conn.QueryScalar<string>(cmd2);

                actual.ShouldBe("{\"fullname\": \"Jeremy Miller\"}");
            }
        }


        public TransformFunctionTests() : base("transforms")
        {
        }
    }


}
