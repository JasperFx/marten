using System;
using System.Collections.Generic;
using System.Linq;
using Marten.Testing.Documents;

namespace Marten.Testing.Examples
{
    public class Searching_Within_Child_Collections
    {
        #region sample_searching_within_child_collections
        public class ClassWithChildCollections
        {
            public Guid Id;

            public IList<User> Users = new List<User>();
            public Company[] Companies = new Company[0];

            public string[] Names;
            public IList<string> NameList;
            public List<string> NameList2;
        }

        public void searching(IDocumentStore store)
        {
            using (var session = store.QuerySession())
            {
                var searchNames = new string[] { "Ben", "Luke" };

                session.Query<ClassWithChildCollections>()
                    // Where collections of deep objects
                    .Where(x => x.Companies.Any(_ => _.Name == "Jeremy"))

                    // Where for Contains() on array of simple types
                    .Where(x => x.Names.Contains("Corey"))

                    // Where for Contains() on List<T> of simple types
                    .Where(x => x.NameList.Contains("Phillip"))

                    // Where for Contains() on IList<T> of simple types
                    .Where(x => x.NameList2.Contains("Jens"))

                    // Where for Any(element == value) on simple types
                    .Where(x => x.Names.Any(_ => _ == "Phillip"))

                    // The Contains() operator on subqueries within Any() searches
                    // only supports constant array of String or Guid expressions.
                    // Both the property being searched (Names) and the values
                    // being compared (searchNames) need to be arrays.
                    .Where(x => x.Names.Any(_ => searchNames.Contains(_)));
            }
        }

        #endregion sample_searching_within_child_collections
    }
}
