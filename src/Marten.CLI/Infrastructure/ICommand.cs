using System.Threading.Tasks;

namespace Marten.CLI.Infrastructure
{
    public interface ICommand : IMessage
    {   
        Task<ICommandContext> Execute(ICommandContext context);
    }
}