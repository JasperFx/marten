using System;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten.Exceptions;
using Marten.Linq;
using Marten.Testing.Harness;

namespace LinqTests.Acceptance;

public class equals_method_usage_validation : IntegrationContext
{
    public equals_method_usage_validation(DefaultStoreFixture fixture) : base(fixture)
    {
    }

    public class QueryTarget
    {
        public int IntProp { get; set; }
        public long LongProp { get; set; }
        public decimal DecimalProp { get; set; }
        public bool BoolProp { get; set; }
        public Guid Id { get; set; }
        public DateTime DateTimeProp { get; set; }
        public DateTimeOffset DateTimeOffsetProp { get; set; }
    }

    [Fact]
    public async Task when_wrong_types_are_compared()
    {
        var queryTarget = new QueryTarget
        {
            Id = System.Guid.NewGuid()
        };
        theSession.Store(queryTarget);

        await theSession.SaveChangesAsync();

        object notInt = "not int";

        Assert.Throws<BadLinqExpressionException>(() =>
        {
            var value = theSession
                .Query<QueryTarget>()
                .FirstOrDefault(x => x.IntProp.Equals(notInt));
        });
    }

    [Fact]
    public async Task when_wrong_types_are_compared_inside_a_negation()
    {
        var queryTarget = new QueryTarget
        {
            Id = System.Guid.NewGuid()
        };
        theSession.Store(queryTarget);

        await theSession.SaveChangesAsync();

        object notInt = "not int";

        Assert.Throws<BadLinqExpressionException>(() =>
        {
            var firstOrDefault = theSession
                .Query<QueryTarget>()
                .FirstOrDefault(x => !x.IntProp.Equals(notInt));
        });
    }

    [Fact]
    public async Task can_use_inside_of_compiled_query()
    {
        var queryTarget = new QueryTarget
        {
            IntProp = 1,
            LongProp = 2,
            DecimalProp = 1.1m,
            BoolProp = true,
            Id = Guid.NewGuid()
        };

        theSession.Store(queryTarget);

        await theSession.SaveChangesAsync();

        var itemFromDb =
            await theSession.QueryAsync(new CompiledQueryTarget() {IdProp = queryTarget.Id, IntProp = queryTarget.IntProp});

        Assert.NotNull(itemFromDb);
    }

    public class CompiledQueryTarget : ICompiledQuery<QueryTarget, QueryTarget>
    {
        public Guid IdProp { get; set; }
        public int IntProp { get; set; }

        public Expression<Func<IMartenQueryable<QueryTarget>, QueryTarget>> QueryIs()
        {
            return q => q.FirstOrDefault(x => x.IntProp.Equals(IntProp) && x.Id.Equals(IdProp));
        }
    }

}
