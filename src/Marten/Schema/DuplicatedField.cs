using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using FubuCore;
using FubuCore.Reflection;
using Marten.Generation;
using Marten.Util;

namespace Marten.Schema
{
    public class DuplicatedField : Field, IField
    {
        private string _columnName;

        public static DuplicatedField For<T>(Expression<Func<T, object>> expression)
        {
            var accessor = ReflectionHelper.GetAccessor(expression);

            // Hokey, but it's just for testing for now.
            if (accessor is PropertyChain)
            {
                throw new NotSupportedException("Not yet supporting deep properties yet. Soon.");
            }


            return new DuplicatedField(new MemberInfo[] {accessor.InnerProperty});

            
        }

        public DuplicatedField(MemberInfo[] memberPath) : base(memberPath)
        {
            ColumnName = MemberName.SplitPascalCase().ToLower().Replace(" ", "_");
        }

        public string ColumnName
        {
            get { return _columnName; }
            set
            {
                _columnName = value;
                SqlLocator = "d." + _columnName;
            }
        }

        public DuplicatedFieldRole Role { get; set; } = DuplicatedFieldRole.Search;

        public UpsertArgument UpsertArgument => new UpsertArgument
        {
            Arg = "arg_" + ColumnName.ToLower(),
            Column = ColumnName.ToLower(),
            PostgresType = TypeMappings.PgTypes[Members.Last().GetMemberType()]
        };

        // I say you don't need a ForeignKey 
        public virtual TableColumn ToColumn(IDocumentSchema schema)
        {
            return new TableColumn(ColumnName, TypeMappings.PgTypes[Members.Last().GetMemberType()]);
        }

        public string WithParameterCode()
        {
            var accessor = Members.Select(x => x.Name + "?").Join("").TrimEnd('?');

            return $".WithParameter(`{UpsertArgument.Arg}`, document.{accessor})".Replace('`', '"');
        }

        public string SqlLocator { get; private set; }
        public string LateralJoinDeclaration { get; } = null;
    }
}