using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Linq;
using Marten.PLv8.Transforms;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.PLv8.Testing.Transforms;

public class select_with_transformation: OneOffConfigurationsContext
{
    public select_with_transformation()
    {
        StoreOptions(_ =>
        {
            _.UseJavascriptTransformsAndPatching(x => x.LoadFile("get_fullname.js"));
        });
    }


    #region sample_using_transform_to_json

    [Fact]
    public void can_select_a_string_field_in_compiled_query()
    {
        var user = new User { FirstName = "Eric", LastName = "Berry" };

        using var session = TheStore.LightweightSession();
        session.Store(user);
        session.SaveChanges();

        var name = session.Query<User>().Select(x => x.FirstName)
            .Single();

        name.ShouldBe("Eric");
    }

    [Fact]
    public async Task can_transform_to_json()
    {
        var user = new User { FirstName = "Eric", LastName = "Berry" };

        await using var session = TheStore.LightweightSession();
        session.Store(user);
        await session.SaveChangesAsync();


        var json = await session.Query<User>()
            .Where(x => x.Id == user.Id)
            .TransformOneToJson("get_fullname");

        json.ShouldBe("{\"fullname\": \"Eric Berry\"}");
    }

    #endregion

    [Fact]
    public async Task can_transform_to_json_async()
    {
        var user = new User { FirstName = "Eric", LastName = "Berry" };

        await using var session = TheStore.LightweightSession();
        session.Store(user);
        await session.SaveChangesAsync();

        var json = await session.Query<User>()
            .Where(x => x.Id == user.Id)
            .TransformOneToJson("get_fullname");

        json.ShouldBe("{\"fullname\": \"Eric Berry\"}");
    }

    #region sample_transform_to_another_type
    public class FullNameView
    {
        public string fullname { get; set; }
    }

    [Fact]
    public async Task can_transform_to_another_doc()
    {
        var user = new User { FirstName = "Eric", LastName = "Berry" };

        await using var session = TheStore.LightweightSession();
        session.Store(user);
        await session.SaveChangesAsync();

        var view = await session.Query<User>()
            .Where(x => x.Id == user.Id)
            .TransformOneTo<FullNameView>("get_fullname");

        view.fullname.ShouldBe("Eric Berry");
    }

    [Fact]
    public async Task can_write_many_to_json()
    {
        var user1 = new User { FirstName = "Eric", LastName = "Berry" };
        var user2 = new User { FirstName = "Derrick", LastName = "Johnson" };

        await using var session = TheStore.LightweightSession();
        session.Store(user1, user2);
        await session.SaveChangesAsync();

        var view = await session.Query<User>()

            .TransformManyToJson("get_fullname");

        view.ShouldBe("[{\"fullname\": \"Eric Berry\"},{\"fullname\": \"Derrick Johnson\"}]");
    }

    #endregion

    [Fact]
    public async Task can_transform_to_another_doc_async()
    {
        var user = new User { FirstName = "Eric", LastName = "Berry" };

        await using var session = TheStore.LightweightSession();
        session.Store(user);
        await session.SaveChangesAsync();

        var view = await session.Query<User>()
            .Where(x => x.Id == user.Id)
            .TransformOneTo<FullNameView>("get_fullname");

        view.fullname.ShouldBe("Eric Berry");
    }

}
