using System.Linq;
using Baseline.Reflection;
using Marten.Linq;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq
{
    public class SelectTransformerTests
    {
        private DocumentMapping theMapping;
        private TargetObject theTarget;
        private SelectTransformer<User2> theSelector;

        public SelectTransformerTests()
        {
            theMapping = DocumentMappingFactory.For<User>();
            theTarget = new TargetObject(typeof(invoking_query_with_select_Tests.User2));
            theTarget.StartBinding(ReflectionHelper.GetProperty<User>(x => x.FirstName)).Members.Add(ReflectionHelper.GetProperty<User2>(x => x.First));
            theTarget.StartBinding(ReflectionHelper.GetProperty<User>(x => x.LastName)).Members.Add(ReflectionHelper.GetProperty<User2>(x => x.Last));

            theSelector = new SelectTransformer<User2>(theMapping, theTarget);
        }


        [Fact]
        public void select_fields_by_json_pairs()
        {
            theSelector.SelectFields().Single()
                .ShouldBe("json_build_object('FirstName', d.data ->> 'First', 'LastName', d.data ->> 'Last') as json");
        }

        public class User2
        {
            public string First { get; set; }
            public string Last { get; set; }
        }

    }
}