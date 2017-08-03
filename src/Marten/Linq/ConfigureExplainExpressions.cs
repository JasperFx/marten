using System.Text;

namespace Marten.Linq
{
    internal sealed class ConfigureExplainExpressions : IConfigureExplainExpressions
    {       
        private readonly StringBuilder toOptions = new StringBuilder();
        public IConfigureExplainExpressions Analyze()
        {
            toOptions.Append("ANALYZE,");
            return this;
        }

        public IConfigureExplainExpressions Verbose()
        {
            toOptions.Append("VERBOSE,");
            return this;
        }

        public IConfigureExplainExpressions Costs()
        {
            toOptions.Append("COSTS,");
            return this;
        }

        public IConfigureExplainExpressions Buffers()
        {
            toOptions.Append("BUFFERS,");
            return this;
        }

        public IConfigureExplainExpressions Timing()
        {
            toOptions.Append("TIMING,");
            return this;
        }

        public override string ToString()
        {
            return toOptions.ToString();
        }
    }
}