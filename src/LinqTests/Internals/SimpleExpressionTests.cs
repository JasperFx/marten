using System;
using System.Linq.Expressions;
using JasperFx.Core;
using Marten;
using Marten.Linq;
using Marten.Linq.Members;
using Marten.Linq.Members.Dictionaries;
using Marten.Linq.Parsing;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;

namespace LinqTests.Internals;

public class SimpleExpressionTests
{
    private static readonly DocumentQueryableMemberCollection Members = new(DocumentMapping.For<Target>(), new StoreOptions());

    private static int Age = 49;
    private string Color = "blue";

    private SimpleExpression parse(Expression<Func<bool>> expression)
    {
        return new SimpleExpression(Members, expression);
    }

    private SimpleExpression parse(Expression<Func<Target,object>> expression)
    {
        Expression body = expression;
        if (expression is LambdaExpression l) body = l.Body;
        if (body is UnaryExpression u && u.NodeType == ExpressionType.Convert) body = u.Operand;
        return new SimpleExpression(Members, body);
    }

    private SimpleExpression parse(Expression<Func<object>> expression)
    {
        Expression body = expression;
        if (expression is LambdaExpression l) body = l.Body;

        return new SimpleExpression(Members, body);
    }

    [Fact]
    public void parse_field_on_current_object()
    {
        var expression = parse(() => Color);

        expression.HasConstant.ShouldBeTrue();
        expression.Constant.Value.ShouldBe(Color);
    }

    [Fact]
    public void parse_static_field_on_current_object()
    {
        var expression = parse(() => Age);

        expression.HasConstant.ShouldBeTrue();
        expression.Constant.Value.ShouldBe(Age);
    }

    [Fact]
    public void parse_variable_from_outside()
    {
        var name = "foo";

        var expression = parse(() => name);

        expression.HasConstant.ShouldBeTrue();
        expression.Constant.Value.ShouldBe("foo");
    }

    [Fact]
    public void parse_variable_from_outside_with_additional_methods()
    {
        var name = "foo";

        var expression = parse(() => name.ToUpper());

        expression.HasConstant.ShouldBeTrue();
        expression.Constant.Value.ShouldBe("FOO");
    }

    [Fact]
    public void parse_from_global_data()
    {
        var expression = parse(() => DateTime.Today.AddDays(1));

        expression.HasConstant.ShouldBeTrue();
        expression.Constant.Value.ShouldBe(DateTime.Today.AddDays(1));
    }

    [Fact]
    public void find_member_shallow()
    {
        var expression = parse(x => x.Double);

        expression.Member.MemberName.ShouldBe(nameof(Target.Double));
    }

    [Fact]
    public void find_member_deeper()
    {
        var expression = parse(x => x.Inner.Double);

        expression.Member.MemberName.ShouldBe(nameof(Target.Double));
    }

    [Fact]
    public void parse_nullable_type_has_value()
    {
        var expression = parse(x => x.NullableBoolean.HasValue);

        expression.HasConstant.ShouldBeFalse();
        expression
            .Member
            .ShouldBeOfType<HasValueMember>()
            .Inner.MemberName
            .ShouldBe(nameof(Target.NullableBoolean));
    }

    [Fact]
    public void find_not_field()
    {
        var expression = parse(x => !x.Flag);

        expression.Comparable.ShouldBeOfType<NotMember>()
            .Inner.ShouldBeOfType<BooleanMember>()
            .MemberName.ShouldBe(nameof(Target.Flag));

    }

    [Fact]
    public void find_collection_count()
    {
        var expression = parse(x => x.Children.Length);

        expression.Member.ShouldBeOfType<CollectionLengthMember>()
            .Parent.MemberName.ShouldBe("Children");
    }

    [Fact]
    public void find_dictionary_item_member()
    {
        var expression = parse(x => x.StringDict["foo"]);

        var member = expression.Member
            .ShouldBeOfType<DictionaryItemMember<string, string>>();

        member.Key.ShouldBe("foo");
        member.RawLocator.ShouldBe("d.data -> 'StringDict' ->> 'foo'");
        member.TypedLocator.ShouldBe("d.data -> 'StringDict' ->> 'foo'");
    }

    [Fact]
    public void find_array_indexer_field_of_string()
    {
        var expression = parse(x => x.StringArray[0]);
        var member = expression.Member.ShouldBeOfType<StringMember>();
        member.RawLocator.ShouldBe("CAST(d.data ->> 'StringArray' as jsonb) ->> 0");
        member.TypedLocator.ShouldBe("CAST(d.data ->> 'StringArray' as jsonb) ->> 0");
    }
}
