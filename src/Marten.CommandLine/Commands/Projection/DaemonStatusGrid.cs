using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Baseline;
using Spectre.Console;

namespace Marten.CommandLine.Commands.Projection
{
    internal class DaemonStatusGrid
    {
        private readonly LightweightCache<string, StoreDaemonStatus> _stores = new(name => new StoreDaemonStatus(name));
        private readonly BatchingBlock<DaemonStatusMessage> _batching;
        private readonly Table _table;
        private LiveDisplayContext _context;

        public DaemonStatusGrid()
        {
            var updates = new ActionBlock<DaemonStatusMessage[]>(UpdateBatch, new ExecutionDataflowBlockOptions
            {
                EnsureOrdered = true,
                MaxDegreeOfParallelism = 1
            });
            _batching = new(100, updates);

            _table = new Table();

            _table.AddColumn("Marten Projections");
            _table.Columns[0].Alignment = Justify.Center;

            var completion = new TaskCompletionSource();

#pragma warning disable VSTHRD110
            AnsiConsole.Live(_table).StartAsync(ctx =>
#pragma warning restore VSTHRD110
            {
                _context = ctx;

                return completion.Task;
            });
        }

        internal void UpdateBatch(DaemonStatusMessage[] messages)
        {
            foreach (var message in messages)
            {
                _stores[message.StoreName].ReadState(message.DatabaseName, message.State);
            }

            var storeTables = _stores.OrderBy(x => x.Name).Select(x => x.BuildTable()).ToArray();

            for (var i = 0; i < storeTables.Length; i++)
            {
                var table = storeTables[i];
                if (_table.Rows.Count < (i + 1))
                {
                    _table.AddRow(table);
                }
                else
                {
                    _table.Rows.Update(i, 0, table);
                }
            }

            _context?.Refresh();
        }

        public void Post(DaemonStatusMessage message)
        {
#pragma warning disable VSTHRD110
            _batching.SendAsync(message);
#pragma warning restore VSTHRD110
        }
    }
}
