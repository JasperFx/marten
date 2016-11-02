using System.Data.Common;
using Marten.Linq;
using Marten.Services;
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
            reader.GetString(0).Returns(json);

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