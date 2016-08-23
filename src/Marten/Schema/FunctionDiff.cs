using System;
using Baseline;
using Marten.Util;

namespace Marten.Schema
{
    public class FunctionDiff
    {
        public FunctionBody Expected { get; set; }
        public FunctionBody Actual { get; set; }

        public FunctionDiff(FunctionBody expected, FunctionBody actual)
        {
            Expected = expected;
            Actual = actual;
        }

        public bool AllNew => Actual == null;
        public bool HasChanged => AllNew || !Expected.Body.CanonicizeSql().Equals(Actual.Body.CanonicizeSql(), StringComparison.OrdinalIgnoreCase);

        public void WritePatch(StoreOptions options, SchemaPatch patch)
        {
            if (AllNew)
            {
                patch.Updates.Apply(this, Expected.Body);

                Expected.DropStatements.Each(drop =>
                {
                    if (!drop.EndsWith("cascade", StringComparison.OrdinalIgnoreCase))
                    {
                        drop = drop.TrimEnd(';') + " cascade;";
                    }

                    patch.Rollbacks.Apply(this, drop);
                });

                writeOwnership(options, patch);

            }
            else if (HasChanged)
            {
                Actual.DropStatements.Each(drop =>
                {
                    if (!drop.EndsWith("cascade", StringComparison.OrdinalIgnoreCase))
                    {
                        drop = drop.TrimEnd(';') + " cascade;";
                    }

                    patch.Updates.Apply(this, drop);
                });

                patch.Updates.Apply(this, Expected.Body);

                Expected.DropStatements.Each(drop =>
                {
                    patch.Rollbacks.Apply(this, drop);
                });

                writeOwnership(options, patch);
            }
        }

        private void writeOwnership(StoreOptions options, SchemaPatch patch)
        {
            if (options.OwnerName.IsEmpty()) return;

            var ownership = Expected.ToOwnershipCommand(options.OwnerName);

            patch.Updates.Apply(this, ownership);
        }

        public override string ToString()
        {
            return Expected.Function.QualifiedName + " Diff";
        }
    }
}