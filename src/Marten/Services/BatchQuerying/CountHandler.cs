using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Npgsql;

namespace Marten.Services.BatchQuerying
{
    public class CountHandler : IDataReaderHandler<long>
    {
        private readonly TaskCompletionSource<long> _source = new TaskCompletionSource<long>();

        public void Configure(NpgsqlCommand command, DocumentQuery query)
        {
            query.ConfigureForCount(command);
        }

        public Task<long> ReturnValue => _source.Task;

        public async Task Handle(DbDataReader reader, CancellationToken token)
        {
            await reader.ReadAsync(token);

            _source.SetResult(reader.GetInt64(0));
        }
    }
}