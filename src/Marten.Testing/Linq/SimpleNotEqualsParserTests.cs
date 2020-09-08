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
	public class SimpleNotEqualsParserTests : IntegrationContext
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
		public void CanTranslateNotEqualsToQueries()
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
				.Where(x => !x.IntProp.Equals(queryTarget.IntProp))
				.Where(x => !x.LongProp.Equals(queryTarget.LongProp))
				.Where(x => !x.DecimalProp.Equals(queryTarget.DecimalProp))
				.Where(x => !x.BoolProp.Equals(queryTarget.BoolProp))
				.Where(x => !x.Id.Equals(queryTarget.Id))
				.Where(x => !x.DateTimeProp.Equals(queryTarget.DateTimeProp))
				.Where(x => !x.DateTimeOffsetProp.Equals(queryTarget.DateTimeOffsetProp))
				.FirstOrDefault();

			Assert.Null(itemFromDb);
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
					.Where(x => !x.IntProp.Equals(notInt))
					.FirstOrDefault();
			});
		}



        public SimpleNotEqualsParserTests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
