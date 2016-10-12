using System;
using System.Diagnostics;
using System.Linq;
using Marten.Linq;
using Marten.Services;
using Shouldly;
using Xunit;
using System.Collections.Generic;

namespace Marten.Testing.Linq
{
    public class Bug_554_Query_Any_or_Count_of_Collection : DocumentSessionFixture<NulloIdentityMap>
    {

        [Fact]
        public void can_query_any_on_array()
        {
            var target1 = new Target { Id = Guid.NewGuid(), GuidList = new List<Guid> { Guid.NewGuid() } };
            var target2 = new Target { Id = Guid.NewGuid(), GuidList = new List<Guid> { }, NumberArray = new int[] { 1, 2, 3 } };
            theSession.Store(target1, target2);
            theSession.SaveChanges();

            theSession.Query<Target>().Count().ShouldBe(2);
            theSession.Query<Target>().Where(x => x.NumberArray.Any()).Count().ShouldBe(1);
        }

        [Fact]
        public void can_query_any_on_list()
        {
            var target1 = new Target { Id = Guid.NewGuid(), GuidList = new List<Guid> { Guid.NewGuid() } };
            var target2 = new Target { Id = Guid.NewGuid(), GuidList = new List<Guid> { }, NumberArray = new int[] { 1, 2, 3 } };
            theSession.Store(target1, target2);
            theSession.SaveChanges();

            theSession.Query<Target>().Count().ShouldBe(2);
            var res = theSession.Query<Target>().Where(x => x.GuidList.Any()).ToList();
            res.Count().ShouldBe(1);
            res.First().Id.ShouldBe(target1.Id);
        }

        [Fact]
        public void can_query_length_of_array()
        {
            var target1 = new Target { Id = Guid.NewGuid(), GuidList = new List<Guid> { Guid.NewGuid() } };
            var target2 = new Target { Id = Guid.NewGuid(), GuidList = new List<Guid> { }, NumberArray = new int[] { 1, 2, 3 } };
            theSession.Store(target1, target2);
            theSession.SaveChanges();

            theSession.Query<Target>()
                .Where(item => item.NumberArray.Length == 3)
                .First()
                .Id
                .ShouldBe(target2.Id);
        }

        [Fact]
        public void can_query_count_of_list()
        {
            var target1 = new Target { Id = Guid.NewGuid(), GuidList = new List<Guid> { Guid.NewGuid() } };
            var target2 = new Target { Id = Guid.NewGuid(), GuidList = new List<Guid> { }, NumberArray = new int[] { 1, 2, 3 } };
            theSession.Store(target1, target2);
            theSession.SaveChanges();

            var res = theSession.Query<Target>()
                .Where(item => item.GuidList.Count() == 1)
                .ToList();
            res.Count().ShouldBe(1);
            res.First().Id.ShouldBe(target1.Id);
        }

        public List<Guid> GuidList { get; set; }
    }
}