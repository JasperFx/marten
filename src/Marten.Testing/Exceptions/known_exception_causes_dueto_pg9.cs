using System;
using System.Linq;
using Marten.Exceptions;
using Marten.Testing.Acceptance;
using Marten.Testing.Documents;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Exceptions
{
    public class known_exception_causes_dueto_pg9: IntegratedFixture
    {
        private readonly bool _hasRequiredMaximumPgVersion;
        private readonly string _skipReason;

        public known_exception_causes_dueto_pg9()
        {
            var requiredMaximumPgVersion = Version.Parse("10.0");
            _hasRequiredMaximumPgVersion =
                theStore.Diagnostics.GetPostgresVersion().CompareTo(requiredMaximumPgVersion) < 0;
            _skipReason = $"Test skipped, maximum Postgres version required is {requiredMaximumPgVersion}";
        }

        [SkippableFact]
        public void can_map_jsonb_FTS_not_supported()
        {
            Skip.IfNot(_hasRequiredMaximumPgVersion, _skipReason);

            var e = Assert.Throws<MartenCommandNotSupportedException>(() =>
            {
                using (var session = theStore.OpenSession())
                {
                    session.Query<User>().Where(x => x.PlainTextSearch("throw")).ToList();
                }
            });

            Assert.Contains(KnownExceptionCause.ToTsvectorOnJsonb.Description, e.Message);
        }

        [SkippableFact]
        public void can_totsvector_other_than_jsonb_without_FTS_exception()
        {
            var e = Assert.Throws<MartenCommandException>(() =>
            {
                using (var session = theStore.OpenSession())
                {
                    session.Query<User>("to_tsvector(?)", 0).ToList();
                }
            });
        }
    }
}
