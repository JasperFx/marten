using System;
using System.Globalization;
using System.Linq;
using Shouldly;
using Marten.Util;

namespace Marten.Testing.Examples
{
    public class LinqExamples
    {
        // SAMPLE: query_for_all
        public void get_all_documents_of_a_type(IDocumentSession session)
        {
            // Calling ToArray() just forces the query to be executed
            var targets = session.Query<Target>().ToArray();
        }
        // ENDSAMPLE


        // SAMPLE: query_by_basic_operators
        public void basic_operators(IDocumentSession session)
        {
            // Field equals a value
            session.Query<Target>().Where(x => x.Number == 5);

            // Field does not equal a value
            session.Query<Target>().Where(x => x.Number != 5);

            // Field compared to values
            session.Query<Target>().Where(x => x.Number > 5);
            session.Query<Target>().Where(x => x.Number >= 5);
            session.Query<Target>().Where(x => x.Number < 5);
            session.Query<Target>().Where(x => x.Number <= 5);
        }
        // ENDSAMPLE

        // SAMPLE: querying_with_and_or_or
        public void and_or(IDocumentSession session)
        {
            // AND queries
            session.Query<Target>().Where(x => x.Number > 0 && x.Number <= 5);

            // OR queries
            session.Query<Target>().Where(x => x.Number == 5 || x.Date == DateTime.Today);
        }
        // ENDSAMPLE

        // SAMPLE: deep_nested_properties
        public void deep_queries(IDocumentSession session)
        {
            session.Query<Target>().Where(x => x.Inner.Number == 3);
        }
        // ENDSAMPLE

        // SAMPLE: searching_within_string_fields
        public void string_fields(IDocumentSession session)
        {
            session.Query<Target>().Where(x => x.String.StartsWith("A"));
            session.Query<Target>().Where(x => x.String.EndsWith("Suffix"));

            session.Query<Target>().Where(x => x.String.Contains("something"));
            session.Query<Target>().Where(x => x.String.Equals("The same thing"));
        }
        // ENDSAMPLE

        // SAMPLE: searching_within_case_insensitive_string_fields
        public void case_insensitive_string_fields(IDocumentSession session)
        {
            session.Query<Target>().Where(x => x.String.StartsWith("A", StringComparison.OrdinalIgnoreCase));
            session.Query<Target>().Where(x => x.String.EndsWith("SuFfiX", StringComparison.OrdinalIgnoreCase));

            // using Marten.Util
            session.Query<Target>().Where(x => x.String.Contains("soMeThiNg", StringComparison.OrdinalIgnoreCase));
        }
        // ENDSAMPLE

        // SAMPLE: ordering-in-linq
        public void order_by(IDocumentSession session)
        {
            // Sort in ascending order
            session.Query<Target>().OrderBy(x => x.Date);

            // Sort in descending order
            session.Query<Target>().OrderByDescending(x => x.Date);

            // You can use multiple order by's
            session.Query<Target>().OrderBy(x => x.Date).ThenBy(x => x.Number);
        }
        // ENDSAMPLE

        // SAMPLE: using_take_and_skip
        public void using_take_and_skip(IDocumentSession session)
        {
            // gets records 11-20 from the database
            session.Query<Target>().Skip(10).Take(10).OrderBy(x => x.Number).ToArray();
        }
        // ENDSAMPLE

        // SAMPLE: select_a_single_value
        public void select_a_single_value(IDocumentSession session)
        {
            // Single()/SingleOrDefault() will throw exceptions if more than 
            // one result is returned from the database
            session.Query<Target>().Where(x => x.Number == 5).Single();
            session.Query<Target>().Where(x => x.Number == 5).SingleOrDefault();

            session.Query<Target>().Where(x => x.Number == 5).OrderBy(x => x.Date).First();
            session.Query<Target>().Where(x => x.Number == 5).OrderBy(x => x.Date).FirstOrDefault();

            session.Query<Target>().Where(x => x.Number == 5).OrderBy(x => x.Date).Last();
            session.Query<Target>().Where(x => x.Number == 5).OrderBy(x => x.Date).LastOrDefault();

            // Using the query inside of Single/Last/First is supported as well
            session.Query<Target>().Single(x => x.Number == 5);
        }
        // ENDSAMPLE

        // SAMPLE: boolean_queries
        public void query_by_booleans(IDocumentSession session)
        {
            // Flag is a boolean property.

            // Where Flag is true
            session.Query<Target>().Where(x => x.Flag).ToArray();
            // or
            session.Query<Target>().Where(x => x.Flag == true).ToArray();

            // Where Flag is false
            session.Query<Target>().Where(x => !x.Flag).ToArray();
            // or
            session.Query<Target>().Where(x => x.Flag == false).ToArray();
        }
        // ENDSAMPLE

        // SAMPLE: query_by_nullable_types
        public void query_by_nullable_type_nulls(IDocumentSession session)
        {
            // You can use Nullable<T>.HasValue in Linq queries
            session.Query<Target>().Where(x => !x.NullableNumber.HasValue).ToArray();
            session.Query<Target>().Where(x => x.NullableNumber.HasValue).ToArray();

            // You can always search by field is NULL
            session.Query<Target>().Where(x => x.Inner == null);
        }
        // ENDSAMPLE



    }
}