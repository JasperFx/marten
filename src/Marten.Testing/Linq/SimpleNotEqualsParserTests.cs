using System;
using System.Collections;
using System.Collections.Generic;
using Xunit;
using System.Linq;
using System.Linq.Expressions;
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

		[Theory]
		[InlineData(PropertySearching.ContainmentOperator)]
		[InlineData(PropertySearching.JSON_Locator_Only)]
		public void NotEqualsGeneratesSameSqlAsNotEqualityOperatorWhenRegardlessOfPropertySearching(PropertySearching search)
		{
			StoreOptions(options =>
			{
				options.Schema.For<QueryTarget>().PropertySearching(search);
			});

			var queryTarget = new QueryTarget
			{
				IntProp = 1,
				LongProp = 2,
				DecimalProp = 1.1m,
				BoolProp = true,
				Id = Guid.NewGuid(),
				DateTimeProp = DateTime.UtcNow
			};

			var queryEquals = theSession.Query<QueryTarget>()
				.Where(x => !x.IntProp.Equals(queryTarget.IntProp))
				.Where(x => !x.LongProp.Equals(queryTarget.LongProp))
				.Where(x => !x.DecimalProp.Equals(queryTarget.DecimalProp))
				.Where(x => !x.BoolProp.Equals(queryTarget.BoolProp))
				.Where(x => !x.Id.Equals(queryTarget.Id))
				.Where(x => !x.DateTimeProp.Equals(queryTarget.DateTimeProp))
				.Where(x => !x.DateTimeOffsetProp.Equals(queryTarget.DateTimeOffsetProp))
				.ToCommand().CommandText;

			var queryEqualOperator = theSession.Query<QueryTarget>()
				.Where(x => x.IntProp != queryTarget.IntProp)
				.Where(x => x.LongProp != queryTarget.LongProp)
				.Where(x => x.DecimalProp != queryTarget.DecimalProp)
				.Where(x => x.BoolProp != queryTarget.BoolProp)
				.Where(x => x.Id != queryTarget.Id)
				.Where(x => x.DateTimeProp != queryTarget.DateTimeProp)
				.Where(x => x.DateTimeOffsetProp != queryTarget.DateTimeOffsetProp)
				.ToCommand().CommandText;

			Assert.Equal(queryEqualOperator, queryEquals);
		}

		[Theory]
		[ClassData(typeof(TestData))]
		public void NotEqualsGeneratesSameSqlAsNotEqualityOperator(object value)
		{
			string queryEquals = null, queryEqualOperator = null;
			switch (value)
			{
				case int intValue:
					queryEquals = theSession.Query<QueryTarget>()
						.Where(x => !x.IntProp.Equals(intValue)).ToCommand().CommandText;
					queryEqualOperator = theSession.Query<QueryTarget>()
						.Where(x => x.IntProp != intValue).ToCommand().CommandText;
					break;
				case Guid guidValue:
					queryEquals = theSession.Query<QueryTarget>()
						.Where(x => !x.Id.Equals(guidValue)).ToCommand().CommandText;
					queryEqualOperator = theSession.Query<QueryTarget>()
						.Where(x => x.Id != guidValue).ToCommand().CommandText;
					break;
				case decimal decimalValue:
					queryEquals = theSession.Query<QueryTarget>()
						.Where(x => !x.DecimalProp.Equals(decimalValue)).ToCommand().CommandText;
					queryEqualOperator = theSession.Query<QueryTarget>()
						.Where(x => x.DecimalProp != decimalValue).ToCommand().CommandText;
					break;
				case long longValue:
					queryEquals = theSession.Query<QueryTarget>()
						.Where(x => !x.LongProp.Equals(longValue)).ToCommand().CommandText;
					queryEqualOperator = theSession.Query<QueryTarget>()
						.Where(x => x.LongProp != longValue).ToCommand().CommandText;
					break;
				case DateTime dateTimeValue:
					queryEquals = theSession.Query<QueryTarget>()
						.Where(x => !x.DateTimeProp.Equals(dateTimeValue)).ToCommand().CommandText;
					queryEqualOperator = theSession.Query<QueryTarget>()
						.Where(x => x.DateTimeProp != dateTimeValue).ToCommand().CommandText;
					break;
				case DateTimeOffset dateTimeOffsetValue:
					queryEquals = theSession.Query<QueryTarget>()
						.Where(x => !x.DateTimeOffsetProp.Equals(dateTimeOffsetValue)).ToCommand().CommandText;
					queryEqualOperator = theSession.Query<QueryTarget>()
						.Where(x => x.DateTimeOffsetProp != dateTimeOffsetValue).ToCommand().CommandText;
					break;
				// Null
				default:
					queryEquals = theSession.Query<QueryTarget>()
						.Where(x => !x.BoolProp.Equals(null)).ToCommand().CommandText;
					queryEqualOperator = theSession.Query<QueryTarget>()
#pragma warning disable CS0472 // The result of the expression is always the same since a value of this type is never equal to 'null'
						.Where(x => x.BoolProp != null).ToCommand().CommandText;
#pragma warning restore CS0472 // The result of the expression is always the same since a value of this type is never equal to 'null'
					break;
			}
			Assert.Equal(queryEqualOperator, queryEquals);
		}

        public SimpleNotEqualsParserTests(DefaultStoreFixture fixture) : base(fixture)
        {
        }
    }
}
