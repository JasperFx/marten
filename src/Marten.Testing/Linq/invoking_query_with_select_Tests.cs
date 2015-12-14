using System;
using System.Linq;
using Marten.Services;
using Marten.Testing.Documents;
using Xunit;

namespace Marten.Testing.Linq
{
    public class invoking_query_with_select_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        [Fact]
        public void use_select_in_query()
        {
            theSession.Store(new User {FirstName = "Hank"});
            theSession.Store(new User {FirstName = "Bill"});
            theSession.Store(new User {FirstName = "Sam"});
            theSession.Store(new User {FirstName = "Tom"});

            theSession.SaveChanges();


            Exception<NotSupportedException>.ShouldBeThrownBy(() =>
            {
                theSession.Query<User>().OrderBy(x => x.FirstName).Select(x => x.FirstName)
                    .ShouldHaveTheSameElementsAs("Bill", "Hank", "Sam", "Tom");
            });


        }
    }
}