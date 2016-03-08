using System.Linq;
using System.Linq.Expressions;
using Baseline;
using Marten.Schema;

namespace Marten.Linq.Handlers
{
    public class StringStartsWith : MethodCallParser<string>
    {
        public StringStartsWith() : base(x => x.StartsWith(null))
        {
        }

        public override IWhereFragment Parse(IDocumentMapping mapping, ISerializer serializer, MethodCallExpression expression)
        {
            var locator = mapping.JsonLocator(expression.Object);
            var value = expression.Arguments.Single().Value().As<string>();
            return new WhereFragment("{0} like ?".ToFormat(locator), value + "%");
        }
    }
}