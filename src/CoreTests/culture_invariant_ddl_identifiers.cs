using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Events.Projections;
using Marten.Events.Projections.Flattened;
using Marten.Schema;
using Marten.Testing.Documents;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql.Functions;
using Xunit;

namespace CoreTests;

public record InvoiceIssued(Guid Id, decimal Amount);

public class InvoiceFlatTableProjection: FlatTableProjection
{
    public InvoiceFlatTableProjection(): base("Invoices", SchemaNameSource.EventSchema)
    {
        Table.AddColumn<Guid>("id").AsPrimaryKey();
        Project<InvoiceIssued>(map => map.Map(x => x.Amount));
    }
}

/// <summary>
/// The follow-up to #5778, which made document aliases culture-invariant: two more identifiers Marten lowered with
/// the current culture, so under tr-TR or az-AZ an uppercase <c>I</c> became a dotless <c>ı</c>.
/// </summary>
public class culture_invariant_ddl_identifiers
{
    private static T underCulture<T>(string culture, Func<T> action)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// Weasel's <c>ReadTemplatesAsync</c> keys a template by its file name lowered invariantly, and
    /// <c>Templates</c> creates an empty template for any key it does not hold. Looked up with a culture-sensitive
    /// <c>ToLower()</c>, a template named with an uppercase <c>I</c> silently resolved to that empty one.
    /// </summary>
    [Theory]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public async Task a_ddl_template_is_found_whatever_the_current_culture(string culture)
    {
        var directory = Path.Combine(Path.GetTempPath(), "marten-ddl-templates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "Invoicing.table"), "GRANT SELECT ON %SCHEMA%.%TABLENAME% TO reporting;");

            var options = new StoreOptions();
            await options.Advanced.Migrator.ReadTemplatesAsync(directory);
            options.Schema.For<Target>().DdlTemplate("Invoicing");

            var mapping = options.Storage.MappingFor(typeof(Target));

            var ddl = underCulture(culture, () =>
            {
                var writer = new StringWriter();
                new DocumentSchema(mapping).WritePermissions(options.Advanced.Migrator, writer);
                return writer.ToString();
            });

            ddl.ShouldContain("GRANT SELECT ON public.mt_doc_target TO reporting;");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("tr-TR")]
    public void a_flat_table_upsert_function_name_is_the_same_whatever_the_current_culture(string culture)
    {
        var options = new StoreOptions();
        var projection = new InvoiceFlatTableProjection();

        var names = underCulture(culture, () =>
            ((IProjectionSchemaSource)projection).CreateSchemaObjects(options.EventGraph)
            .OfType<Function>()
            .Select(x => x.Identifier.Name)
            .ToArray());

        names.ShouldBe(["mt_upsert_invoices_invoiceissued"]);
    }
}
