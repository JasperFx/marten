using System.Collections.Generic;
using Marten.Linq;
using Marten.Linq.Compiled;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq.Compiled
{
    public class DictionaryElementTests
    {
        [Fact]
        public void can_set_field_one_deep()
        {
            var field = FindMembers.Member<DictTarget>(x => x.Field);

            var element = new DictionaryElement<DictTarget, string>(EnumStorage.AsString, new []{ "foo" }, field);

            var target = new DictTarget
            {
                Field = "bar"
            };

            var dict = new Dictionary<string, object>();

            element.Write(target, dict);

            dict["foo"].ShouldBe("bar");
        }

        [Fact]
        public void can_set_property_one_deep()
        {
            var prop = FindMembers.Member<DictTarget>(x => x.Property);

            var element = new DictionaryElement<DictTarget, string>(EnumStorage.AsString, new[] { "foo" }, prop);

            var target = new DictTarget
            {
                Property = "baz"
            };

            var dict = new Dictionary<string, object>();

            element.Write(target, dict);

            dict["foo"].ShouldBe("baz");
        }


        [Fact]
        public void can_set_property_two_deep()
        {
            var prop = FindMembers.Member<DictTarget>(x => x.Property);

            var element = new DictionaryElement<DictTarget, string>(EnumStorage.AsString, new[] { "one", "two" }, prop);

            var target = new DictTarget
            {
                Property = "baz"
            };

            var dict = new Dictionary<string, object>();

            element.Write(target, dict);

            dict["one"].ShouldBeOfType<Dictionary<string, object>>()["two"]
                .ShouldBe("baz");

        }

        [Fact]
        public void can_set_property_three_deep()
        {
            var prop = FindMembers.Member<DictTarget>(x => x.Property);

            var element = new DictionaryElement<DictTarget, string>(EnumStorage.AsString, new[] { "one", "two", "three" }, prop);

            var target = new DictTarget
            {
                Property = "baz"
            };

            var dict = new Dictionary<string, object>();

            element.Write(target, dict);

            dict["one"].ShouldBeOfType<Dictionary<string, object>>()
                ["two"].ShouldBeOfType<Dictionary<string, object>>()
                ["three"].ShouldBe("baz");

        }

        [Fact]
        public void can_set_property_three_deep_to_existing_dictionary_structure()
        {
            var prop = FindMembers.Member<DictTarget>(x => x.Property);

            var element = new DictionaryElement<DictTarget, string>(EnumStorage.AsString, new[] { "one", "two", "three" }, prop);

            var target = new DictTarget
            {
                Property = "baz"
            };

            var dict = new Dictionary<string, object>();
            dict.Add("one", new Dictionary<string, object>());

            element.Write(target, dict);

            dict["one"].ShouldBeOfType<Dictionary<string, object>>()
                ["two"].ShouldBeOfType<Dictionary<string, object>>()
                ["three"].ShouldBe("baz");

        }


        public class DictTarget
        {
            public string Field;
            public string Property { get; set; }
        }
    }
}