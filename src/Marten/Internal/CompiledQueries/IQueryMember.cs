using System;
using System.Reflection;
using LamarCodeGeneration;
using Npgsql;

namespace Marten.Internal.CompiledQueries
{
    public interface IQueryMember
    {
        Type Type { get; }
        bool CanWrite();

        MemberInfo Member { get; }
        int ParameterIndex { get; set; }
        void GenerateCode(GeneratedMethod method, StoreOptions storeOptions);
        void StoreValue(object query);
        void TryMatch(NpgsqlCommand command, StoreOptions storeOptions);
        void TryWriteValue(UniqueValueSource valueSource, object query);
        object GetValueAsObject(object query);
    }
}
