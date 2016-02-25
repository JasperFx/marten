using System.Threading.Tasks;
using Marten.Services;
using Npgsql;
using Shouldly;
using StructureMap;
using Xunit;

namespace Marten.Testing.Services
{
    public class ManagedConnectionTests
    {
        [Fact]
        public async Task increments_the_request_count()
        {
            using (var connection = new ManagedConnection(new ConnectionSource()))
            {
                connection.RequestCount.ShouldBe(0);

                connection.Execute(cmd => { });
                connection.RequestCount.ShouldBe(1);

                connection.Execute(new NpgsqlCommand(), c => { });
                connection.RequestCount.ShouldBe(2);

                connection.Execute(c => "");
                connection.RequestCount.ShouldBe(3);

                connection.Execute(new NpgsqlCommand(), c => "");
                connection.RequestCount.ShouldBe(4);



                await connection.ExecuteAsync(async (c, t) => { });
                connection.RequestCount.ShouldBe(5);

                await connection.ExecuteAsync(new NpgsqlCommand(), async (c, t) => {});
                connection.RequestCount.ShouldBe(6);

                await connection.ExecuteAsync(async (c, t) => "");
                connection.RequestCount.ShouldBe(7);

                await connection.ExecuteAsync(new NpgsqlCommand(),async (c, t) => "");
                connection.RequestCount.ShouldBe(8);

            }
        } 
    }
}