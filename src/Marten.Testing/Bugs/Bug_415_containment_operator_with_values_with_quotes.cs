using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_415_containment_operator_with_values_with_quotes : IntegratedFixture
    {
        [Fact]
        public void can_query_when_matched_value_has_quotes()
        {
            using (var session = theStore.QuerySession())
            {
                session.Query<Course>().Any(x => x.ExtIds.Contains("some'thing")).ShouldBeFalse();
            }
        }

        [Fact]
        public void can_query_inside_of_non_primitive_collection()
        {
            using (var session = theStore.QuerySession())
            {
                session.Query<Course>().Any(x => x.Sources.Any(_ => _.Case == "some'thing"));
            }
        }


        public class Course
        {
            public Guid Id { get; set; }

            public string[] ExtIds { get; set; }

            public IList<Source> Sources { get; set; }
        }

        public class Source
        {
            public string Case { get; set; }
        }
    }
}