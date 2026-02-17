using System.Globalization;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;

namespace Marten.Linq.SqlGeneration
{
    public class CollatedFragment: ISqlFragment
    {
        private readonly ISqlFragment _inner;
        private readonly CultureInfo _culture;

        public CollatedFragment(ISqlFragment inner, CultureInfo culture)
        {
            _inner = inner;
            _culture = culture;
        }

        public void Apply(ICommandBuilder builder)
        {
            builder.Append("(");
            _inner.Apply(builder);
            builder.Append($") COLLATE \"{_culture.Name}\"");
        }
    }
}
