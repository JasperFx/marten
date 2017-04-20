using System;
using System.IO;
using System.Linq.Expressions;
using Baseline;
using Marten.Schema;
using Marten.Services;
using Marten.Storage;
using Xunit;

namespace Marten.Testing.Schema
{
    public class do_not_overwrite_tables_with_searchable_fields_Tests : DocumentSessionFixture<NulloIdentityMap>
    {
        private void searchable(Expression<Func<Target, object>> expression)
        {
            StoreOptions(_ =>
            {
                _.Schema.For<Target>().Duplicate(expression);
            });

            theStore.DefaultTenant.StorageFor(typeof(Target)).ShouldNotBeNull();

            var existing = theStore.TableSchema(typeof (Target));

            var configured = new DocumentTable(theStore.DefaultTenant.MappingFor(typeof(Target)).As<DocumentMapping>());

            if (!existing.Equals(configured))
            {
                
                var writer = new StringWriter();
                writer.WriteLine("Expected:");
                configured.Write(theStore.Schema.StoreOptions.DdlRules, writer);
                writer.WriteLine();
                writer.WriteLine("But from the database, was:");
                existing.Write(theStore.Schema.StoreOptions.DdlRules, writer);

                throw new Exception(writer.ToString());
            }
        }

        [Fact]
        public void string_fields()
        {
            searchable(x => x.String);
        }

        [Fact]
        public void int_field()
        {
            searchable(x => x.Number);
        }

        [Fact]
        public void long_field()
        {
            searchable(x => x.Long);
        }

        [Fact]
        public void bool_field()
        {
            searchable(x => x.Flag);
        }

        [Fact]
        public void datetime_field()
        {
            searchable(x => x.Date);
        }

        [Fact]
        public void dateoffset_field()
        {
            searchable(x => x.DateOffset);
        }

        [Fact]
        public void decimal_field()
        {
            searchable(x => x.Decimal);
        }

        [Fact]
        public void double_field()
        {
            searchable(x => x.Double);
        }

        [Fact]
        public void guid_fields()
        {
            searchable(x => x.OtherGuid);
        }
    }
}