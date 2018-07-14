using System;
using System.Linq;
using Baseline;
using Baseline.Dates;
using Marten.Util;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Bugs
{
    public class Bug_432_querying_with_UTC_times_with_offset : IntegratedFixture
    {
        public Bug_432_querying_with_UTC_times_with_offset(ITestOutputHelper output) : base(output)
        {
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

                var sql = "select public.mt_immutable_timestamp(d.data ->> \'DateTimeField\') as time from public.mt_doc_dateclass as d";

                using (var reader = session.Connection.CreateCommand().Sql(sql).ExecuteReader())
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

                var sql = "select public.mt_immutable_timestamp(d.data ->> \'dateTimeField\') as time from public.mt_doc_dateclass as d";

                using (var reader = session.Connection.CreateCommand().Sql(sql).ExecuteReader())
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

                var sql = "select public.mt_immutable_timestamp(d.data ->> \'date_time_field\') as time from public.mt_doc_dateclass as d";

                using (var reader = session.Connection.CreateCommand().Sql(sql).ExecuteReader())
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
                var now = GenerateTestDateTime();
                _output.WriteLine("now: " + now.ToString("o"));
                var testClass = new DateOffsetClass
                {
                    Id = Guid.NewGuid(),
                    DateTimeField = now
                };

                session.Store(testClass);

                session.Store(new DateOffsetClass
                {
                    DateTimeField = now.Add(5.Minutes())
                });

                session.Store(new DateOffsetClass
                {
                    DateTimeField = now.Add(-5.Minutes())
                });

                session.SaveChanges();

                var cmd = session.Query<DateOffsetClass>().Where(x => now >= x.DateTimeField)
                    .ToCommand();

                _output.WriteLine(cmd.CommandText);

                session.Query<DateOffsetClass>().ToList().Each(x =>
                {
                    _output.WriteLine(x.DateTimeField.ToString("o"));
                });

                session.Query<DateOffsetClass>()
                    .Count(x => now >= x.DateTimeField).ShouldBe(2);
            }
        }

        [Fact]
        public void can_issue_queries_against_the_datetime_offset_as_duplicate_field()
        {
            StoreOptions(_ => _.Schema.For<DateOffsetClass>().Duplicate(x => x.DateTimeField));

            using (var session = theStore.LightweightSession())
            {
                var now = GenerateTestDateTime();
                _output.WriteLine("now: " + now.ToString("o"));
                var testClass = new DateOffsetClass
                {
                    Id = Guid.NewGuid(),
                    DateTimeField = now
                };

                session.Store(testClass);

                session.Store(new DateOffsetClass
                {
                    DateTimeField = now.Add(5.Minutes())
                });

                session.Store(new DateOffsetClass
                {
                    DateTimeField = now.Add(-5.Minutes())
                });

                session.SaveChanges();

                var cmd = session.Query<DateOffsetClass>().Where(x => now >= x.DateTimeField)
                    .ToCommand();

                _output.WriteLine(cmd.CommandText);

                session.Query<DateOffsetClass>().ToList().Each(x =>
                {
                    _output.WriteLine(x.DateTimeField.ToString("o"));
                });

                session.Query<DateOffsetClass>()
                    .Count(x => now >= x.DateTimeField).ShouldBe(2);
            }
        }

        private static DateTime GenerateTestDateTime()
        {
            var now = DateTime.UtcNow;
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
        public DateTimeOffset DateTimeField { get; set; }
    }
}