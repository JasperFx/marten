using System;
using System.Linq;
using System.Reflection;
using Baseline;
using LamarCodeGeneration;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;
using Weasel.Core;
using Weasel.Postgresql;

namespace Marten.Internal.CompiledQueries
{
    public interface IQueryMember<T>: IQueryMember
    {
        T GetValue(object query);
        void SetValue(object query, T value);
        T Value { get; }
    }

    public abstract class QueryMember<T> : IQueryMember<T>
    {
        protected QueryMember(MemberInfo member)
        {
            Member = member;
        }

        public Type Type => typeof(T);

        public object GetValueAsObject(object query)
        {
            return GetValue(query);
        }

        public abstract T GetValue(object query);
        public abstract void SetValue(object query, T value);

        public void StoreValue(object query)
        {
            Value = GetValue(query);
        }

        public void TryMatch(NpgsqlCommand command, StoreOptions storeOptions)
        {
            ParameterIndex = -1;

            if (Type.IsEnum)
            {
                var parameterValue = storeOptions.Serializer().EnumStorage == EnumStorage.AsInteger
                    ? Value.As<int>()
                    : (object)Value.ToString();

                tryToFind(command, parameterValue);
            }

            // These methods are on the ClonedCompiledQuery base class, and are
            // used to set the right parameters
            if (!tryToFind(command, Value) && Type == typeof(string))
            {
                if (tryToFind(command, $"%{Value}"))
                {
                    Mask = "StartsWith({0})";
                }
                else if (tryToFind(command, $"%{Value}%"))
                {
                    Mask = "ContainsString({0})";
                }
                else if (tryToFind(command, $"{Value}%"))
                {
                    Mask = "EndsWith({0})";
                }
            }


        }

        private bool tryToFind(NpgsqlCommand command, object value)
        {
            var parameter = command.Parameters.FirstOrDefault(x => value.Equals(x.Value));
            if (parameter != null)
            {
                ParameterIndex = command.Parameters.IndexOf(parameter);
                return true;
            }

            return false;
        }

        public string Mask { get; set; }

        public void TryWriteValue(UniqueValueSource valueSource, object query)
        {
            if (CanWrite())
            {
                var value = (T)valueSource.GetValue(typeof(T));
                Value = value;
                SetValue(query, value);
            }
        }

        public T Value { get; private set; }

        public abstract bool CanWrite();

        public MemberInfo Member { get; }

        public int ParameterIndex { get; set; }

        public void GenerateCode(GeneratedMethod method, StoreOptions storeOptions)
        {
            if (Type.IsEnum)
            {
                if (storeOptions.Serializer().EnumStorage == EnumStorage.AsInteger)
                {
                    method.Frames.Code($@"
parameters[{ParameterIndex}].NpgsqlDbType = {{0}};
parameters[{ParameterIndex}].Value = (int)_query.{Member.Name};
", NpgsqlDbType.Integer);
                }
                else
                {
                    method.Frames.Code($@"
parameters[{ParameterIndex}].NpgsqlDbType = {{0}};
parameters[{ParameterIndex}].Value = _query.{Member.Name}.ToString();
", NpgsqlDbType.Varchar);
                }
            }
            else if (Mask == null)
            {
                generateBasicSetter(method);
            }
            else
            {
                generateMaskedStringCode(method);
            }


        }

        private void generateMaskedStringCode(GeneratedMethod method)
        {
            var maskedValue = Mask.ToFormat($"_query.{Member.Name}");

            method.Frames.Code($@"
parameters[{ParameterIndex}].NpgsqlDbType = {{0}};
parameters[{ParameterIndex}].Value = {maskedValue};
", PostgresqlProvider.Instance.ToParameterType(Member.GetMemberType()));
        }

        private void generateBasicSetter(GeneratedMethod method)
        {
            method.Frames.Code($@"
parameters[{ParameterIndex}].NpgsqlDbType = {{0}};
parameters[{ParameterIndex}].Value = _query.{Member.Name};
", PostgresqlProvider.Instance.ToParameterType(Member.GetMemberType()));
        }
    }
}
