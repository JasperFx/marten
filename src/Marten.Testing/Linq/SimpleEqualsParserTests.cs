using System;
using System.Collections;
using System.Collections.Generic;
using Xunit;
using System.Linq;
using System.Linq.Expressions;
using Marten.Exceptions;
using Marten.Services;
using Marten.Linq;
using Marten.Schema;
using Marten.Testing.Harness;

namespace Marten.Testing.Linq
{
	public class SimpleEqualsParserTests : IntegrationContext
	{
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
		public void CanTranslateEqualsToQueries()
		{
			var queryTarget = new QueryTarget
			{
				IntProp = 1,
				LongProp = 2,
				DecimalProp = 1.1m,
				BoolProp = true,
				Id = Guid.NewGuid(),
				DateTimeProp = DateTime.UtcNow,
				DateTimeOffsetProp = DateTimeOffset.UtcNow
			};

			theSession.Store(queryTarget);

			theSession.SaveChanges();

			var itemFromDb = theSession.Query<QueryTarget>()
				.Where(x => x.IntProp.Equals(queryTarget.IntProp))
				.Where(x => x.LongProp.Equals(queryTarget.LongProp))
				.Where(x => x.DecimalProp.Equals(queryTarget.DecimalProp))
				.Where(x => x.BoolProp.Equals(queryTarget.BoolProp))
				.Where(x => x.Id.Equals(queryTarget.Id))
				.Where(x => x.DateTimeProp.Equals(queryTarget.DateTimeProp))
				.Where(x => x.DateTimeOffsetProp.Equals(queryTarget.DateTimeOffsetProp))
				.FirstOrDefault();

			Assert.NotNull(itemFromDb);
		}

		[Fact]
		public void ThrowsWhenValueNotConvertibleToComparandType()
		{
			var queryTarget = new QueryTarget
			{
				Id = System.Guid.NewGuid()
			};
			theSession.Store(queryTarget);

			theSession.SaveChanges();

			object notInt = "not int";

			Assert.Throws<BadLinqExpressionException>(() =>
			{
				theSession.Query<QueryTarget>()
					.Where(x => x.IntProp.Equals(notInt))
					.FirstOrDefault();
			});
		}


		public class TestData : IEnumerable<object[]>
		{
			private readonly List<object[]> _data = new List<object[]>
			{
				new object[] { Guid.NewGuid() },
				new object[] { 0 },
				new object[] { null },
				new object[] { false },
				new object[] { 32m },
				new object[] { 0L },
				new object[] { DateTime.UtcNow },
				new object[] { DateTimeOffset.UtcNow },
			};

			public IEnumerator<object[]> GetEnumerator()
			{
				return _data.GetEnumerator();
			}

			IEnumerator IEnumerable.GetEnumerator()
			{
				return GetEnumerator();
			}
		}

		[Fact]
		public void CanUseEqualsInCompiledQuery()
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

			theSession.SaveChanges();

			var itemFromDb =
				theSession.Query(new CompiledQueryTarget() {IdProp = queryTarget.Id, IntProp = queryTarget.IntProp});

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

        public SimpleEqualsParserTests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
