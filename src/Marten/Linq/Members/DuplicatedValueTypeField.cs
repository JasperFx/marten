using System.Linq.Expressions;
using JasperFx.Core.Reflection;
using Marten.Schema.Arguments;
using Weasel.Core;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.Members;

public class DuplicatedValueTypeField: DuplicatedField
{
    public DuplicatedValueTypeField(EnumStorage enumStorage, QueryableMember innerMember, ValueTypeInfo valueTypeInfo
        , bool useTimestampWithoutTimeZoneForDateTime = true, bool notNull = false): base(
        enumStorage, innerMember, useTimestampWithoutTimeZoneForDateTime, notNull)
    {
        ValueTypeInfo = valueTypeInfo;
    }

    public ValueTypeInfo ValueTypeInfo { get; }

    internal override UpsertArgument UpsertArgument
    {
        get
        {
            var upsertArgument = base.UpsertArgument;

            if (InnerMember.Member.GetRawMemberType()!.IsNullable())
            {
                upsertArgument.ParameterValue = $"{InnerMember.Member.Name}.Value.{ValueTypeInfo.ValueProperty.Name}";
            }

            upsertArgument.ParameterValue = $"{InnerMember.Member.Name}.{ValueTypeInfo.ValueProperty.Name}";

            return upsertArgument;
        }
    }

    public override ISqlFragment CreateComparison(string op, ConstantExpression constant)
    {
        return InnerMember.CreateComparison(op, constant);
    }
}
