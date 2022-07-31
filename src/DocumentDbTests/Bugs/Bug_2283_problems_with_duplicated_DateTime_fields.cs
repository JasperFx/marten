using System;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Exceptions;
using Marten.Schema;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;

namespace DocumentDbTests.Bugs
{
    public class Bug_2283_problems_with_duplicated_DateTime_fields : BugIntegrationContext
    {
        [Fact]
        public async Task can_query_on_duplicated_datetime_field()
        {
            StoreOptions(opts =>
            {
                opts.Schema.For<MyModel>().Duplicate(x => x.Date);
                opts.UseDefaultSerialization();
            });
            var model1 = new MyModel { UserId = Guid.NewGuid(), Date = DateTime.Now };

            theSession.Store(model1);
            await theSession.SaveChangesAsync();

            await Should.ThrowAsync<InvalidDateTimeUsageException>(async () =>
            {
                var minDate = await theSession
                    .Query<MyModel>()
                    .Where(t => t.UserId == model1.UserId)
                    .MinAsync(t => t.Date);
            });


        }

    }

    public class MyModel
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }

        public DateTime Date { get; set; }
    }
}
