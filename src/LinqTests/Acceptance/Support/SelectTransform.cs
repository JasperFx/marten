using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using FastExpressionCompiler;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shouldly;

namespace LinqTests.Acceptance.Support;

public class SelectTransform<T>: LinqTestCase
{
    private readonly Expression<Func<Target, T>> _selector;

    public SelectTransform(Expression<Func<Target, T>> selector)
    {
        _selector = selector;
    }

    public override async Task Compare(IQuerySession session, Target[] documents, TestOutputMartenLogger logger)
    {
        var target = documents.FirstOrDefault(x => x.StringArray?.Length > 0 && x.NumberArray?.Length > 0 && x.Inner != null);
        var expected = documents.Select(_selector.CompileFast()).Take(1).Single();

        var actual = await session.Query<Target>().Where(x => x.Id == target.Id).Select(_selector).SingleAsync();

        var expectedJson = JsonConvert.SerializeObject(expected);
        var actualJson = JsonConvert.SerializeObject(actual);

        if (!JToken.DeepEquals(JObject.Parse(expectedJson), JObject.Parse(actualJson)))
        {
            // This would you would assume throw
            actualJson.ShouldBe(expectedJson);
        }
    }
}
