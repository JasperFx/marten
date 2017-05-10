using System.Linq;
using Shouldly;
using Xunit;
using System.Collections.Generic;
using System.Reflection;

namespace Marten.Testing.Linq
{
    public class query_by_different_id_member
    {
        private static List<string> listIds = new List<string>() { "qwe", "zxc" };
        private static List<string> listSystemIds = new List<string>() { "123", "456" };

        [Fact]
        public void get_by_id_property()
        {
            int resultId1;
            int resultId2;

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                var memberInfo = typeof(BaseClass).GetMember("SystemId").FirstOrDefault();
                _.MappingFor(typeof(BaseClass)).IdMember = memberInfo;
            }))
            {
                store.BulkInsert(new BaseClass[] {
                    new BaseClass() { Id = "qwe", SystemId = "123" },
                    new BaseClass() { Id = "asd", SystemId = "456" },
                    new BaseClass() { Id = "zxc", SystemId = "789" }
                });

                using (var session = store.QuerySession())
                {
                    var martenQueryable = session.Query<BaseClass>();
                    resultId1 = martenQueryable.Count(o => listIds.Contains(o.Id));
                    resultId2 = martenQueryable.Count(o => listSystemIds.Contains(o.Id));
                }
            }

            resultId1.ShouldBe(2);
            resultId2.ShouldBe(0);
        }

        [Fact]
        public void get_by_id_member_property()
        {
            int resultSystemId3;
            int resultSystemId4;

            using (var store = DocumentStore.For(_ =>
            {
                _.Connection(ConnectionSource.ConnectionString);
                var memberInfo = typeof(BaseClass).GetMember("SystemId").FirstOrDefault();
                _.MappingFor(typeof(BaseClass)).IdMember = memberInfo;
            }))
            {
                store.BulkInsert(new BaseClass[] {
                    new BaseClass() { Id = "qwe", SystemId = "123" },
                    new BaseClass() { Id = "asd", SystemId = "456" },
                    new BaseClass() { Id = "zxc", SystemId = "789" }
                });

                using (var session = store.QuerySession())
                {
                    var martenQueryable = session.Query<BaseClass>();
                    resultSystemId3 = martenQueryable.Count(o => listIds.Contains(o.SystemId));
                    resultSystemId4 = martenQueryable.Count(o => listSystemIds.Contains(o.SystemId));
                }
            }

            resultSystemId3.ShouldBe(0);
            resultSystemId4.ShouldBe(2);
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
