using System.Linq;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class query_by_different_id_member : IntegratedFixture
    {
        private static readonly string[] listIds = {"qwe", "zxc"};
        private static readonly string[] listSystemIds = {"123", "456"};

        [Fact]
        public void get_by_id_member_property()
        {
            StoreOptions(_ => { _.Schema.For<BaseClass>().Identity(x => x.SystemId); });

            theStore.BulkInsert(new[]
            {
                new BaseClass {Id = "qwe", SystemId = "123"},
                new BaseClass {Id = "asd", SystemId = "456"},
                new BaseClass {Id = "zxc", SystemId = "789"}
            });

            using (var session = theStore.QuerySession())
            {
                session.Query<BaseClass>().Count(x => x.Id.IsOneOf(listSystemIds)).ShouldBe(0);
                session.Query<BaseClass>().Count(x => x.SystemId.IsOneOf(listSystemIds)).ShouldBe(2);
            }
        }

        [Fact]
        public void get_by_id_property()
        {
            StoreOptions(_ => { _.Schema.For<BaseClass>().Identity(x => x.SystemId); });

            theStore.BulkInsert(new[]
            {
                new BaseClass {Id = "qwe", SystemId = "123"},
                new BaseClass {Id = "asd", SystemId = "456"},
                new BaseClass {Id = "zxc", SystemId = "789"}
            });

            using (var session = theStore.QuerySession())
            {
                session.LoadMany<BaseClass>(listSystemIds).Count.ShouldBe(2);

                session.Query<BaseClass>().Count(x => x.Id.IsOneOf(listIds)).ShouldBe(2);
                session.Query<BaseClass>().Count(x => x.SystemId.IsOneOf(listSystemIds)).ShouldBe(2);

                session.Query<BaseClass>().Count(x => x.Id.IsOneOf("123", "456")).ShouldBe(0);
                session.Query<BaseClass>().Count(x => x.SystemId.IsOneOf("qwe", "zxc")).ShouldBe(0);
            }
        }
    }


    public interface IIdentity
    {
        string Id { get; set; }
    }

    public interface IStorableObject : IIdentity
    {
        string SystemId { get; set; }
        int Version { get; set; }
    }

    public abstract class PocoStorableObject : IStorableObject
    {
        public string Id { get; set; }
        public string SystemId { get; set; }
        public int Version { get; set; }
    }

    public class BaseClass : PocoStorableObject
    {
        public string Name { get; set; }
    }

    public class Auto : BaseClass
    {
        public string Model { get; set; }
    }

    public class Animal : BaseClass
    {
        public string Kind { get; set; }
    }
}