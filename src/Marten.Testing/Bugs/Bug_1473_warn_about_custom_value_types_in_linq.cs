using System.Linq;
using Marten.Exceptions;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_1473_warn_about_custom_value_types_in_linq : IntegrationContext
    {
        public Bug_1473_warn_about_custom_value_types_in_linq(DefaultStoreFixture fixture) : base(fixture)
        {
        }

        [Fact]
        public void get_a_descriptive_exception_message()
        {
            var ex = Should.Throw<BadLinqExpressionException>(() =>
            {
                theSession.Query<MyClass>().Where(x => x.CustomObject == new CustomObject()).ToList();
            });

            ex.Message.ShouldBe("Marten cannot support custom value types in Linq expression. Please query on either simple properties of the value type, or register a custom IFieldSource for this value type.");
        }
    }

    public class MyClass
    {
        public string Id { get; set; }
        public CustomObject CustomObject { get; set; }
    }

    public class CustomObject
    {
        public string Name { get; set; }
    }
}
