using System;
using System.Linq;
using Baseline;
using Baseline.Dates;
using Weasel.Postgresql;
using Marten.Testing.Harness;
using Marten.Util;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Bugs
{
    public class Bug_432_querying_with_UTC_times_with_offset: BugIntegrationContext
    {
        private readonly ITestOutputHelper _output;

        public Bug_432_querying_with_UTC_times_with_offset(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void can_issue_queries_against_DateTime()
        {
            using (var session = theStore.LightweightSession())
            {
                var now = GenerateTestDateTime();
                _output.WriteLine("now: " + now.ToString("o"));
                var testClass = new DateClass
                {
                    Id = Guid.NewGuid(),
                    DateTimeField = now
                };

                session.Store(testClass);

                session.Store(new DateClass
                {
                    DateTimeField = now.Add(5.Minutes())
                });

                session.Store(new DateClass
                {
                    DateTimeField = now.Add(-5.Minutes())
                });

                session.SaveChanges();

                var cmd = session.Query<DateClass>().Where(x => now >= x.DateTimeField)
                    .ToCommand();

                _output.WriteLine(cmd.CommandText);

                var sql = $"select {SchemaName}.mt_immutable_timestamp(d.data ->> \'DateTimeField\') as time from {SchemaName}.mt_doc_dateclass as d";

                using (var reader = session.Connection.CreateCommand(sql).ExecuteReader())
                {
                    while (reader.Read())
                    {
                        _output.WriteLine("stored: " + reader.GetDateTime(0).ToString("o"));
                    }
                }

                session.Query<DateClass>().ToList().Each(x =>
                {
                    _output.WriteLine(x.DateTimeField.ToString("o"));
                });

                session.Query<DateClass>()
                    .Count(x => now >= x.DateTimeField).ShouldBe(2);
            }
        }

        [Fact]
        public void can_issue_queries_against_DateTime_with_camel_casing()
        {
            StoreOptions(_ => _.UseDefaultSerialization(casing: Casing.CamelCase));

            using (var session = theStore.LightweightSession())
            {
                var now = GenerateTestDateTime();
                _output.WriteLine("now: " + now.ToString("o"));
                var testClass = new DateClass
                {
                    Id = Guid.NewGuid(),
                    DateTimeField = now
                };

                session.Store(testClass);

                session.Store(new DateClass
                {
                    DateTimeField = now.Add(5.Minutes())
                });

                session.Store(new DateClass
                {
                    DateTimeField = now.Add(-5.Minutes())
                });

                session.SaveChanges();

                var cmd = session.Query<DateClass>().Where(x => now >= x.DateTimeField)
                    .ToCommand();

                _output.WriteLine(cmd.CommandText);

                var sql = $"select {SchemaName}.mt_immutable_timestamp(d.data ->> \'dateTimeField\') as time from {SchemaName}.mt_doc_dateclass as d";

                using (var reader = session.Connection.CreateCommand(sql).ExecuteReader())
                {
                    while (reader.Read())
                    {
                        _output.WriteLine("stored: " + reader.GetDateTime(0).ToString("o"));
                    }
                }

                session.Query<DateClass>().ToList().Each(x =>
                {
                    _output.WriteLine(x.DateTimeField.ToString("o"));
                });

                session.Query<DateClass>()
                    .Count(x => now >= x.DateTimeField).ShouldBe(2);
            }
        }

        [Fact]
        public void can_issue_queries_against_DateTime_with_snake_casing()
        {
            StoreOptions(_ => _.UseDefaultSerialization(casing: Casing.SnakeCase));

            using (var session = theStore.LightweightSession())
            {
                var now = GenerateTestDateTime();
                _output.WriteLine("now: " + now.ToString("o"));
                var testClass = new DateClass
                {
                    Id = Guid.NewGuid(),
                    DateTimeField = now
                };

                session.Store(testClass);

                session.Store(new DateClass
                {
                    DateTimeField = now.Add(5.Minutes())
                });

                session.Store(new DateClass
                {
                    DateTimeField = now.Add(-5.Minutes())
                });

                session.SaveChanges();

                var cmd = session.Query<DateClass>().Where(x => now >= x.DateTimeField)
                    .ToCommand();

                _output.WriteLine(cmd.CommandText);

                var sql = $"select {SchemaName}.mt_immutable_timestamp(d.data ->> \'date_time_field\') as time from {SchemaName}.mt_doc_dateclass as d";

                using (var reader = session.Connection.CreateCommand(sql).ExecuteReader())
                {
                    while (reader.Read())
                    {
                        _output.WriteLine("stored: " + reader.GetDateTime(0).ToString("o"));
                    }
                }

                session.Query<DateClass>().ToList().Each(x =>
                {
                    _output.WriteLine(x.DateTimeField.ToString("o"));
                });

                session.Query<DateClass>()
                    .Count(x => now >= x.DateTimeField).ShouldBe(2);
            }
        }

        [Fact]
        public void can_issue_queries_against_DateTime_as_duplicated_column()
        {
            StoreOptions(_ => _.Schema.For<DateClass>().Duplicate(x => x.DateTimeField));

            using (var session = theStore.LightweightSession())
            {
                var now = GenerateTestDateTime();
                _output.WriteLine("now: " + now.ToString("o"));
                var testClass = new DateClass
                {
                    Id = Guid.NewGuid(),
                    DateTimeField = now
                };

                session.Store(testClass);

                session.Store(new DateClass
                {
                    DateTimeField = now.Add(5.Minutes())
                });

                session.Store(new DateClass
                {
                    DateTimeField = now.Add(-5.Minutes())
                });

                session.SaveChanges();

                var cmd = session.Query<DateClass>().Where(x => now >= x.DateTimeField)
                    .ToCommand();

                _output.WriteLine(cmd.CommandText);

                session.Query<DateClass>().ToList().Each(x =>
                {
                    _output.WriteLine(x.DateTimeField.ToString("o"));
                });

                session.Query<DateClass>()
                    .Count(x => now >= x.DateTimeField).ShouldBe(2);
            }
        }

        [Fact]
        public void can_issue_queries_against_the_datetime_offset()
        {
            using (var session = theStore.LightweightSession())
            {
                var now = GenerateTestDateTimeOffset();
                _output.WriteLine("now: " + now.ToString("o"));
                var testClass = new DateOffsetClass
                {
                    Id = Guid.NewGuid(),
                    DateTimeOffsetField = now
                };

                session.Store(testClass);

                session.Store(new DateOffsetClass
                {
                    DateTimeOffsetField = now.Add(5.Minutes())
                });

                session.Store(new DateOffsetClass
                {
                    DateTimeOffsetField = now.Add(-5.Minutes())
                });

                session.SaveChanges();

                var cmd = session.Query<DateOffsetClass>().Where(x => now >= x.DateTimeOffsetField)
                    .ToCommand();

                _output.WriteLine(cmd.CommandText);

                session.Query<DateOffsetClass>().ToList().Each(x =>
                {
                    _output.WriteLine(x.DateTimeOffsetField.ToString("o"));
                });

                session.Query<DateOffsetClass>()
                    .Count(x => now >= x.DateTimeOffsetField).ShouldBe(2);
            }
        }

        [Fact]
        public void can_issue_queries_against_the_datetime_offset_as_duplicate_field()
        {
            StoreOptions(_ => _.Schema.For<DateOffsetClass>().Duplicate(x => x.DateTimeOffsetField));

            using (var session = theStore.LightweightSession())
            {
                var now = GenerateTestDateTimeOffset();
                _output.WriteLine("now: " + now.ToString("o"));
                var testClass = new DateOffsetClass
                {
                    Id = Guid.NewGuid(),
                    DateTimeOffsetField = now
                };

                session.Store(testClass);

                session.Store(new DateOffsetClass
                {
                    DateTimeOffsetField = now.Add(5.Minutes())
                });

                session.Store(new DateOffsetClass
                {
                    DateTimeOffsetField = now.Add(-5.Minutes())
                });

                session.SaveChanges();

                var cmd = session.Query<DateOffsetClass>().Where(x => now >= x.DateTimeOffsetField)
                    .ToCommand();

                _output.WriteLine(cmd.CommandText);

                session.Query<DateOffsetClass>().ToList().Each(x =>
                {
                    _output.WriteLine(x.DateTimeOffsetField.ToString("o"));
                });

                session.Query<DateOffsetClass>()
                    .Count(x => now >= x.DateTimeOffsetField).ShouldBe(2);
            }
        }

        private static DateTime GenerateTestDateTime()
        {
            var now = DateTime.Now;
            return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
        }

        private static DateTimeOffset GenerateTestDateTimeOffset()
        {
            var now = DateTimeOffset.UtcNow;
            return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
        }
    }

    public class DateClass
    {
        public Guid Id { get; set; }
        public DateTime DateTimeField { get; set; }
    }

    public class DateOffsetClass
    {
        public Guid Id { get; set; }
        public DateTimeOffset DateTimeOffsetField { get; set; }
    }
}
