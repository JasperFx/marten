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
                    execute(store, input);
                }

                return true;
            }
            finally
            {
                input.WriteLogFileIfRequested();
            }
        }

        protected abstract void execute(IDocumentStore store, T input);
    }
}