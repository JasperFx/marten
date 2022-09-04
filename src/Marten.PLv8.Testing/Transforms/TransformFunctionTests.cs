using System;
using System.Linq;
using Baseline;
using Marten.PLv8.Transforms;
using Marten.Testing;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using NpgsqlTypes;
using Shouldly;
using Weasel.Postgresql;
using Xunit;
using Issue = Marten.Testing.Documents.Issue;

namespace Marten.PLv8.Testing.Transforms;

public class TransformFunctionTests : OneOffConfigurationsContext
{
    private readonly string _getFullnameJs = AppContext.BaseDirectory.AppendPath("get_fullname.js");

    private readonly string _binAllsql = AppContext.BaseDirectory.AppendPath("bin", "allsql");
    private readonly string _binAllsql2 = AppContext.BaseDirectory.AppendPath("bin", "allsql2");


    [Fact]
    public async void writes_transform_function()
    {
        var file = _binAllsql.AppendPath("transforms.sql");

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
            await store.Storage.WriteCreationScriptToFile(file);
        }

        var lines = new FileSystem().ReadStringFromFile(file);

        lines.ShouldContain(
            "CREATE OR REPLACE FUNCTION public.mt_transform_get_fullname(doc JSONB) RETURNS JSONB AS $$", Case.Insensitive);
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

        using var conn = theStore.Tenancy.Default.Database.CreateConnection();
        conn.Open();
        conn.CreateCommand(func.GenerateFunction()).ExecuteNonQuery();

        var actual = (string)conn.CreateCommand("select mt_transform_get_fullname(:json)")
            .With("json", json, NpgsqlDbType.Jsonb)
            .ExecuteScalar();

        actual.ShouldBe("{\"fullname\": \"Jeremy Miller\"}");
    }

}


public class MembersJoined
{
    public MembersJoined()
    {
    }

    public MembersJoined(int day, string location, params string[] members)
    {
        Day = day;
        Location = location;
        Members = members;
    }

    public Guid QuestId { get; set; }

    public int Day { get; set; }

    public string Location { get; set; }

    public string[] Members { get; set; }

    public override string ToString()
    {
        return $"Members {Members.Join(", ")} joined at {Location} on Day {Day}";
    }

    protected bool Equals(MembersJoined other)
    {
        return QuestId.Equals(other.QuestId) && Day == other.Day && Location == other.Location && Members.SequenceEqual(other.Members);
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((MembersJoined) obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(QuestId, Day, Location, Members);
    }
}