using System.Collections.Generic;
using System.Reflection;
using Marten.Linq;
using Marten.Linq.Compiled;
using Marten.Services;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;
using Shouldly;
using Xunit;

namespace Marten.Testing.Linq.Compiled
{
    public class ContainmentParameterSetterTests
    {
        [Fact]
        public void add_element_for_field()
        {
            var field = FindMembers.Member<Target>(x => x.StringField);

            var setter = new ContainmentParameterSetter<Target>(new JsonNetSerializer(), new MemberInfo[0]);

            setter.AddElement(new[] { "position" }, field);

            setter.Elements[0].Member.Name.ShouldBe(nameof(Target.StringField));
        }

        [Fact]
        public void add_element_for_property()
        {
            var property = FindMembers.Member<Target>(x => x.String);

            var setter = new ContainmentParameterSetter<Target>(new JsonNetSerializer(), new MemberInfo[0]);

            setter.AddElement(new[] { "position" }, property);

            setter.Elements[0].Member.Name.ShouldBe(nameof(Target.String));
        }

        [Fact]
        public void can_build_out_dictionary_with_a_constant()
        {
            var setter = new ContainmentParameterSetter<Target>(new JsonNetSerializer(), new MemberInfo[0]);
            setter.Constant(new string[] { "foo", "bar" }, "baz");

            var target = new Target
            {
                Color = Colors.Blue,
                String = "Ronald McDonald",
                Number = 5
            };

            var dict = setter.BuildDictionary(target);

            dict["foo"].ShouldBeOfType<Dictionary<string, object>>()
                ["bar"].ShouldBe("baz");
        }

        [Fact]
        public void can_build_out_the_dictionary()
        {
            var serializer = new JsonNetSerializer { EnumStorage = EnumStorage.AsString };

            var setter = new ContainmentParameterSetter<Target>(serializer, new MemberInfo[0]);

            setter.AddElement(new[] { "color" }, FindMembers.Member<Target>(x => x.Color));
            setter.AddElement(new[] { "name" }, FindMembers.Member<Target>(x => x.String));
            setter.AddElement(new[] { "rank" }, FindMembers.Member<Target>(x => x.Number));

            var target = new Target
            {
                Color = Colors.Blue,
                String = "Ronald McDonald",
                Number = 5
            };

            var dict = setter.BuildDictionary(target);

            dict["color"].ShouldBe(Colors.Blue);
            dict["name"].ShouldBe(target.String);
            dict["rank"].ShouldBe(5);
        }

        [Fact]
        public void can_add_a_parameter_to_a_db_command()
        {
            var serializer = new JsonNetSerializer { EnumStorage = EnumStorage.AsString };

            var setter = new ContainmentParameterSetter<Target>(serializer, new MemberInfo[] { FindMembers.Member<Target>(x => x.Children) });

            setter.AddElement(new[] { "color" }, FindMembers.Member<Target>(x => x.Color));
            setter.AddElement(new[] { "name" }, FindMembers.Member<Target>(x => x.String));
            setter.AddElement(new[] { "rank" }, FindMembers.Member<Target>(x => x.Number));

            var target = new Target
            {
                Color = Colors.Blue,
                String = "Ronald McDonald",
                Number = 5
            };

            var command = new NpgsqlCommand();
            var builder = new CommandBuilder(command);

            var parameter = setter.AddParameter(target, builder);

            parameter.NpgsqlDbType.ShouldBe(NpgsqlDbType.Jsonb);
            parameter.Value.ShouldBe("{\"Children\":[{\"color\":\"Blue\",\"name\":\"Ronald McDonald\",\"rank\":5}]}");
        }
    }
}
