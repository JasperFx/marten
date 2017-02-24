using System;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using Marten.Linq;
using Marten.Testing;

namespace Marten.Storyteller.Fixtures
{
    public class QueryList<TInput, TResult>
    {
        private readonly string _suffix;

        public QueryList(string listName, string suffix = "")
        {
            ListName = listName;
            _suffix = suffix;
        }

        public string ListName { get; }

        internal readonly IList<string> QueryNames = new List<string>();
        internal readonly IList<Func<TInput, TResult>> Queries = new List<Func<TInput, TResult>>();

        internal Func<TInput, TResult> FuncFor(string query)
        {
            var index = int.Parse(query);

            return Queries[index - 1];
        }

        public static QueryList<TInput, TResult> operator +(QueryList<TInput, TResult> result, Func<TInput, TResult> query)
        {
            result.Queries.Add(query);
            return result;
        }

        internal void ReadFile(MartenFixture fixture)
        {
            
            new FileSystem().ReadTextFile(fixture.CodeFile, line =>
            {

                if (line.StartsWith(ListName) && line.Contains("+="))
                {
                    var start = line.IndexOf("+=") + 2;

                    var query = line.Substring(start).TrimStart().TrimEnd(';');
                    query = query.Replace("docs => ", "").Replace("docs.", "session.Query<Target>().");

                    QueryNames.Add(query + _suffix);
                }
            });

            fixture.AddQueryListValues(ListName, QueryNames);
        }
    }
}