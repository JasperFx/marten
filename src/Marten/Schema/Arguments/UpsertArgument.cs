using System;
using System.Linq;
using System.Reflection;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Util;
using Npgsql;
using NpgsqlTypes;
using Weasel.Postgresql;
using System.Diagnostics.CodeAnalysis;

namespace Marten.Schema.Arguments;

// Public for code generation, just let it go.
[UnconditionalSuppressMessage("Trimming", "IL2075",
    Justification = "Class-level: PublicMethods/PublicProperties access via a Type obtained from object.GetType() / GetGenericArguments. Source instance is preserved at the StoreOptions / projection-registration boundary.")]
[UnconditionalSuppressMessage("AOT", "IL3050",
    Justification = "Class-level: uses Type.MakeGenericType / MethodInfo.MakeGenericMethod / Activator.CreateInstance / FastExpressionCompiler — runtime code generation. AOT consumers pre-generate codegen artifacts (codegen write) and supply source-generator-backed serializer impls per the AOT publishing guide.")]
public class UpsertArgument
{
    protected static readonly MethodInfo writeMethod =
        typeof(NpgsqlBinaryImporter).GetMethods().FirstOrDefault(x =>
            x.Name == "Write" && x.GetParameters().Length == 2 &&
            x.GetParameters()[0].ParameterType.IsGenericParameter &&
            x.GetParameters()[1].ParameterType == typeof(NpgsqlDbType))!;

    private MemberInfo[] _members;
    private string _postgresType;
    public string Arg { get; set; }

    public string PostgresType
    {
        get => _postgresType;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            _postgresType = value.Contains('(')
                ? value.Split('(')[0].Trim()
                : value;
        }
    }

    public string Column { get; set; }

    public MemberInfo[] Members
    {
        get => _members;
        set
        {
            _members = value;
            if (value != null)
            {
                var memberType = value.Last().GetMemberType();
                DbType = PostgresqlProvider.Instance.ToParameterType(memberType);

                if (_members.Length == 1)
                {
                    var lastMember = _members.Last();
                    DotNetType = lastMember.GetRawMemberType()!;
                    DeclaringType = lastMember.DeclaringType;
                }
                else
                {
                    var rawType = _members.LastOrDefault().GetRawMemberType();
                    if (!rawType.IsClass && !rawType.IsNullable())
                    {
                        DotNetType = typeof(Nullable<>).MakeGenericType(rawType);
                    }
                    else
                    {
                        DotNetType = rawType;
                    }
                }

                ParameterValue = _members.Select(x => x.Name).Join("?.");
            }
        }
    }

    public string ParameterValue { get; set; }
    public Type? DeclaringType { get; set; }

    public Type DotNetType { get; private set; }

    public NpgsqlDbType DbType { get; set; }

    public string ArgumentDeclaration()
    {
        return $"{Arg} {PostgresType}";
    }
}
