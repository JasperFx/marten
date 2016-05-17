using System.Linq;
using System.Reflection;
using Baseline.Reflection;
using Marten.Schema;
using Marten.Testing.Documents;
using Marten.Testing.Fixtures;
using Shouldly;
using Xunit;

namespace Marten.Testing.Schema
{
    public class JsonLocatorFieldTests
    {
        public readonly JsonLocatorField theStringField = JsonLocatorField.For<User>(EnumStorage.AsInteger, x => x.FirstName);
        public readonly JsonLocatorField theNumberField = JsonLocatorField.For<User>(EnumStorage.AsInteger, x => x.Age);
        public readonly JsonLocatorField theEnumField = JsonLocatorField.For<Target>(EnumStorage.AsInteger, x => x.Color);

        [Fact]
        public void member_name_is_derived()
        {
            theStringField.MemberName.ShouldBe("FirstName");
        }

        [Fact]
        public void has_the_member_path()
        {
            theStringField.Members.Single().ShouldBeAssignableTo<PropertyInfo>()
                .Name.ShouldBe("FirstName");
        }

        [Fact]
        public void locator_for_string()
        {
            theStringField.SqlLocator.ShouldBe("d.data ->> 'FirstName'");
        }

        [Fact]
        public void field_for_strings_or_number_are_not_containment()
        {
            theStringField.ShouldUseContainmentOperator().ShouldBeFalse();
            theNumberField.ShouldUseContainmentOperator().ShouldBeFalse();
        }

        [Fact]
        public void make_datetime_fields_be_containment()
        {
            JsonLocatorField.For<Target>(EnumStorage.AsInteger, x => x.Date)
                .ShouldUseContainmentOperator().ShouldBeTrue();

            JsonLocatorField.For<Target>(EnumStorage.AsInteger, x => x.DateOffset)
                .ShouldUseContainmentOperator().ShouldBeTrue();
        }

        [Fact]
        public void locator_for_number()
        {
            theNumberField.SqlLocator.ShouldBe("CAST(d.data ->> 'Age' as integer)");
        }

        [Fact]
        public void locator_for_enum_in_integer_mode()
        {
            theEnumField.SqlLocator.ShouldBe("CAST(d.data ->> 'Color' as integer)");
        }

        [Fact]
        public void locator_for_enum_in_string_mode()
        {
            var field = JsonLocatorField.For<Target>(EnumStorage.AsString, x => x.Color);
            field.SqlLocator.ShouldBe("d.data ->> 'Color'");
        }


        [Fact]
        public void two_deep_members_json_locator()
        {
            var inner = ReflectionHelper.GetProperty<Target>(x => x.Inner);
            var number = ReflectionHelper.GetProperty<Target>(x => x.Number);

            var twodeep = new JsonLocatorField(EnumStorage.AsInteger, new MemberInfo[] {inner, number});

            twodeep.SqlLocator.ShouldBe("CAST(d.data -> 'Inner' ->> 'Number' as integer)");
        }


        [Fact]
        public void three_deep_members_json_locator()
        {
            var inner = ReflectionHelper.GetProperty<Target>(x => x.Inner);
            var number = ReflectionHelper.GetProperty<Target>(x => x.Number);

            var deep = new JsonLocatorField(EnumStorage.AsInteger, new MemberInfo[] { inner, inner, number });

            deep.SqlLocator.ShouldBe("CAST(d.data -> 'Inner' -> 'Inner' ->> 'Number' as integer)");
        }

        [Fact]
        public void three_deep_members_json_locator_for_string_property()
        {
            var inner = ReflectionHelper.GetProperty<Target>(x => x.Inner);
            var stringProp = ReflectionHelper.GetProperty<Target>(x => x.String);

            var deep = new JsonLocatorField(EnumStorage.AsInteger, new MemberInfo[] { inner, inner, stringProp });

            deep.SqlLocator.ShouldBe("d.data -> 'Inner' -> 'Inner' ->> 'String'");
        }
    }
}