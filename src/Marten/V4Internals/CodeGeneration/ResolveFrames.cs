using System.Data.Common;
using System.Threading;
using LamarCodeGeneration.Frames;
using LamarCodeGeneration.Model;

namespace Marten.V4Internals
{
    public static class DbDataReaderCalls
    {
        public static MethodCall ForSync<T>(int position, string returnVariableName)
        {
            var call = MethodCall.For<DbDataReader>(x => x.GetFieldValue<T>(position));
            call.Arguments[0] = new Variable(typeof(int), position.ToString());
            call.ReturnVariable.OverrideName(returnVariableName);

            return call;
        }

        public static MethodCall ForAsync<T>(int position, string returnVariableName)
        {
            var call = MethodCall.For<DbDataReader>(x => x.GetFieldValueAsync<T>(position, CancellationToken.None));
            call.Arguments[0] = new Variable(typeof(int), position.ToString());
            call.ReturnVariable.OverrideName(returnVariableName);

            return call;
        }
    }
}
