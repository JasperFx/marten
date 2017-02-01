using System;
using System.Linq.Expressions;
using System.Reflection;
using Marten.Linq.SoftDeletes;
using Marten.Schema;
using Marten.Services;
using Marten.Util;
using Npgsql;
using Xunit;

namespace Marten.Testing.Linq.SoftDeletes
{
    public class DeletedSinceParserTests
    {
        private readonly DocumentMapping _mapping;
        private readonly MethodCallExpression _expression;
        private readonly DeletedSinceParser _parser;

        public DeletedSinceParserTests()
        {
            _mapping = new DocumentMapping(typeof(object), new StoreOptions()) {DeleteStyle = DeleteStyle.SoftDelete};
            _expression = Expression.Call(typeof(SoftDeletedExtensions).GetMethod(nameof(SoftDeletedExtensions.DeletedSince)),
                Expression.Parameter(typeof(object)),
                Expression.Constant(DateTimeOffset.UtcNow));
            _parser = new DeletedSinceParser();
        }

        [Fact]
        public void WhereFragmentContainsExpectedExpression()
        {
            var result = _parser.Parse(_mapping, new JsonNetSerializer(), _expression);

            var builder = new CommandBuilder(new NpgsqlCommand());

            result.ToSql(builder).ShouldContain("d.mt_deleted and d.mt_deleted_at >");
        }

        [Fact]
        public void ThrowsIfDocumentMappingNotSoftDeleted()
        {
            _mapping.DeleteStyle = DeleteStyle.Remove;

            Exception<NotSupportedException>.ShouldBeThrownBy(() => _parser.Parse(_mapping, new JsonNetSerializer(), _expression));
        }
    }
}
