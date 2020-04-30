using System.Data.Common;
using System.IO;
using Marten.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using NSubstitute;
using Xunit;

namespace Marten.Testing.Linq
{
    public class DeserializeSelectorTests
    {
        [Fact]
        public void resolve_deserializes_the_first_field()
        {
            var reader = Substitute.For<DbDataReader>();
            var target = Target.Random();
            var serializer = new JsonNetSerializer();
            var json = serializer.ToJson(target);

            var selector = new DeserializeSelector<Target>(serializer);
            reader.GetTextReader(0).Returns(new StringReader(json));

            selector.Resolve(reader, null, null).ShouldNotBeNull();
        }

        [Fact]
        public void the_selected_fields()
        {
            var serializer = new JsonNetSerializer();

            var selector = new DeserializeSelector<Target>(serializer);

            selector.SelectFields().ShouldHaveTheSameElementsAs("data");
        }
    }
}
