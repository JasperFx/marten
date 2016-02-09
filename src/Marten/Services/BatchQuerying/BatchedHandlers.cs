using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Marten.Linq;
using Npgsql;
using Remotion.Linq;

namespace Marten.Services.BatchQuerying
{
    public interface IDataReaderHandler<T> : IDataReaderHandler
    {
        void Configure(NpgsqlCommand command, DocumentQuery query);
        Task<T> ReturnValue { get; }

    }

    public class AnyHandler : IDataReaderHandler<bool>
    {
        private readonly TaskCompletionSource<bool> _source = new TaskCompletionSource<bool>(); 

        public void Configure(NpgsqlCommand command, DocumentQuery query)
        {
            query.ConfigureForAny(command);
        }

        public Task<bool> ReturnValue => _source.Task;

        public async Task Handle(DbDataReader reader, CancellationToken token)
        {
            await reader.ReadAsync(token);

            _source.SetResult(reader.GetBoolean(0));
        }
    }

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