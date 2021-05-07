using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Oakton;

namespace Marten.CommandLine
{
    public abstract class MartenCommand<T>: OaktonAsyncCommand<T> where T : MartenInput
    {
        public override async Task<bool> Execute(T input)
        {
            try
            {
                using var host = input.BuildHost();
                var store = host.Services.GetRequiredService<IDocumentStore>();
                return await execute(store, input);
            }
            finally
            {
                input.WriteLogFileIfRequested();
            }
        }

        protected abstract Task<bool> execute(IDocumentStore store, T input);
    }
}
