using Oakton;

namespace Marten.CommandLine
{
    public abstract class MartenCommand<T> : OaktonCommand<T> where T : MartenInput
    {
        public override bool Execute(T input)
        {
            try
            {
                using (var store = input.CreateStore())
                {
                    return execute(store, input);
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