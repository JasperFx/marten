using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using JasperFx;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten;
using Marten.Linq;
using Marten.Linq.Members;
using Marten.Linq.Parsing;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql.SqlGeneration;

namespace LinqTests.Acceptance;

public class custom_linq_extensions
{
    #region sample_using_custom_linq_parser

    [Fact]
    public async Task query_with_custom_parser()
    {
        using var store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionSource.ConnectionString);

            // IsBlue is a custom parser I used for testing this
            opts.Linq.MethodCallParsers.Add(new IsBlue());
            opts.AutoCreateSchemaObjects = AutoCreate.All;

            // This is just to isolate the test
            opts.DatabaseSchemaName = "isblue";
        });

        await store.Advanced.Clean.DeleteAllDocumentsAsync();

        var targets = new List<ColorTarget>();
        for (var i = 0; i < 25; i++)
        {
            targets.Add(new ColorTarget {Color = "Blue"});
            targets.Add(new ColorTarget {Color = "Green"});
            targets.Add(new ColorTarget {Color = "Red"});
        }

        var count = targets.Count(x => x.Color.IsBlue());

        targets.Each(x => x.Id = Guid.NewGuid());

        await store.BulkInsertAsync(targets.ToArray());

        using var session = store.QuerySession();
        session.Query<ColorTarget>().Count(x => x.Color.IsBlue())
            .ShouldBe(count);
    }

    #endregion
}

public class ColorTarget
{
    public string Color { get; set; }
    public Guid Id { get; set; }
}

public static class CustomExtensions
{
    #region sample_custom-extension-for-linq

    public static bool IsBlue(this string value)
    {
        return value == "Blue";
    }

    #endregion
}

#region sample_IsBlue

public class IsBlue: IMethodCallParser
{
    private static readonly PropertyInfo _property = ReflectionHelper.GetProperty<ColorTarget>(x => x.Color);

    public bool Matches(MethodCallExpression expression)
    {
        return expression.Method.Name == nameof(CustomExtensions.IsBlue);
    }

    public ISqlFragment Parse(IQueryableMemberCollection memberCollection, IReadOnlyStoreOptions options,
        MethodCallExpression expression)
    {
        var locator = memberCollection.MemberFor(expression).TypedLocator;

        return new WhereFragment($"{locator} = 'Blue'");
    }
}

#endregion
