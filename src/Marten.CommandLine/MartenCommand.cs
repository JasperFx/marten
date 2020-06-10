using Microsoft.Extensions.DependencyInjection;
using Oakton;

namespace Marten.CommandLine
{
    public abstract class MartenCommand<T>: OaktonCommand<T> where T : MartenInput
    {
        public override bool Execute(T input)
        {
            try
            {
                using (var host = input.BuildHost())
                {
                    using (var store = host.Services.GetRequiredService<IDocumentStore>())
                    {
                        return execute(store, input);
                    }
                }
            }
            finally
            {
                input.WriteLogFileIfRequested();
            }
        }

        protected abstract bool execute(IDocumentStore store, T input);
    }
}
