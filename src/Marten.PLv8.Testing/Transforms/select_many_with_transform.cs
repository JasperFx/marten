using System;
using System.Linq;
using System.Threading.Tasks;
using Marten.PLv8.Transforms;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Marten.Testing.Linq;
using Shouldly;
using Xunit;

namespace Marten.PLv8.Testing.Transforms
{
    [Collection("transforms")]
    public class select_many_with_transform : OneOffConfigurationsContext
    {
        public select_many_with_transform() : base("transforms")
        {
        }


        [Fact]
        public async Task project_select_many_with_javascript()
        {
             StoreOptions(_ =>
             {
                 _.UseJavascriptTransformsAndPatching(x => x.LoadFile("get_target_float.js"));
             });

             var targets = Target.GenerateRandomData(100).ToArray();
             await theStore.BulkInsertAsync(targets);

             using var query = theStore.OpenSession();
             var count = targets
                 .Where(x => x.Flag)
                 .SelectMany(x => x.Children)
                 .Count(x => x.Color == Colors.Green);

             var jsonList = await query.Query<Target>()
                 .Where(x => x.Flag)
                 .SelectMany(x => x.Children)
                 .Where(x => x.Color == Colors.Green)
                 .TransformManyTo<TargetNumbers>("get_target_float");

             jsonList.Count.ShouldBe(count);
        }

    }
}
