using Marten.Linq;
using Marten.Linq.Compiled;
using Marten.Services;
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

            var setter = new ContainmentParameterSetter<Target>(new JsonNetSerializer());

            setter.AddElement(new [] {"position"}, field);

            setter.Elements[0].Member.Name.ShouldBe(nameof(Target.StringField));
        }

        [Fact]
        public void add_element_for_property()
        {
            var property = FindMembers.Member<Target>(x => x.String);

            var setter = new ContainmentParameterSetter<Target>(new JsonNetSerializer());

            setter.AddElement(new[] { "position" }, property);

            setter.Elements[0].Member.Name.ShouldBe(nameof(Target.String));
        }

        [Fact]
        public void can_build_out_the_dictionary()
        {
            var serializer = new JsonNetSerializer {EnumStorage = EnumStorage.AsString};

            var setter = new ContainmentParameterSetter<Target>(serializer);

            setter.AddElement(new [] {"color"}, FindMembers.Member<Target>(x => x.Color));
            setter.AddElement(new [] {"name"}, FindMembers.Member<Target>(x => x.String));
            setter.AddElement(new [] {"rank"}, FindMembers.Member<Target>(x => x.Number));

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

            var setter = new ContainmentParameterSetter<Target>(serializer);

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

            var parameter = setter.AddParameter(target, command);

            parameter.NpgsqlDbType.ShouldBe(NpgsqlDbType.Jsonb);
            parameter.Value.ShouldBe("{\"color\":\"Blue\",\"name\":\"Ronald McDonald\",\"rank\":5}");
        }



    }
}