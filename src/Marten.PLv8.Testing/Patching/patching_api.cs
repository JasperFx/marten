using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten.Events;
using Marten.Events.Projections;
using Marten.PLv8.Patching;
using Marten.Storage;
using Marten.Testing.Documents;
using Marten.Testing.Events;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.SqlGeneration;
using Xunit;

namespace Marten.PLv8.Testing.Patching
{
    [Collection("patching_api")]
    public class patching_api: OneOffConfigurationsContext
    {
        public patching_api() : base("patching_api")
        {
            StoreOptions(_ =>
            {
                _.UseDefaultSerialization(EnumStorage.AsString);
                _.UseJavascriptTransformsAndPatching();
            });
        }

        [Fact]
        public async Task can_use_patch_api_when_autocreate_is_none()
        {
            await theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            var entity = Target.Random();
            theSession.Store(entity);
            await theSession.SaveChangesAsync();



            var store = SeparateStore(o =>
            {
                o.Connection(ConnectionSource.ConnectionString);
                o.UseDefaultSerialization(EnumStorage.AsString);
                o.AutoCreateSchemaObjects = AutoCreate.None;
                o.UseJavascriptTransformsAndPatching();
            });

            using (var session = store.LightweightSession())
            {
                session.Patch<Target>(entity.Id).Set(t => t.String, "foo");
                await session.SaveChangesAsync();
            }
        }

        #region sample_set_an_immediate_property_by_id

        [Fact]
        public void set_an_immediate_property_by_id()
        {
            var target = Target.Random(true);
            target.Number = 5;

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Set(x => x.Number, 10);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).Number.ShouldBe(10);
            }
        }

        #endregion sample_set_an_immediate_property_by_id

        [Fact]
        public void initialise_a_new_property_by_expression()
        {
            theSession.Store(Target.Random(), Target.Random(), Target.Random());
            theSession.SaveChanges();

            #region sample_initialise_a_new_property_by_expression
            const string where = "where (data ->> 'UpdatedAt') is null";
            theSession.Query<Target>(where).Count.ShouldBe(3);
            theSession.Patch<Target>(new WhereFragment(where)).Set("UpdatedAt", DateTime.UtcNow);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Query<Target>(where).Count.ShouldBe(0);
            }
            #endregion sample_initialise_a_new_property_by_expression
        }

        [Fact]
        public void set_a_deep_property_by_id()
        {
            var target = Target.Random(true);
            target.Inner.Number = 5;

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Set(x => x.Inner.Number, 10);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).Inner.Number.ShouldBe(10);
            }
        }

        [Fact]
        public void set_an_immediate_property_by_where_clause()
        {
            var target1 = new Target { Color = Colors.Blue, Number = 1 };
            var target2 = new Target { Color = Colors.Blue, Number = 1 };
            var target3 = new Target { Color = Colors.Blue, Number = 1 };
            var target4 = new Target { Color = Colors.Green, Number = 1 };
            var target5 = new Target { Color = Colors.Green, Number = 1 };
            var target6 = new Target { Color = Colors.Red, Number = 1 };

            theSession.Store(target1, target2, target3, target4, target5, target6);
            theSession.SaveChanges();

            #region sample_set_an_immediate_property_by_where_clause
            // Change every Target document where the Color is Blue
            theSession.Patch<Target>(x => x.Color == Colors.Blue).Set(x => x.Number, 2);
            #endregion sample_set_an_immediate_property_by_where_clause

            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                // These should have been updated
                query.Load<Target>(target1.Id).Number.ShouldBe(2);
                query.Load<Target>(target2.Id).Number.ShouldBe(2);
                query.Load<Target>(target3.Id).Number.ShouldBe(2);

                // These should not because they didn't match the where clause
                query.Load<Target>(target4.Id).Number.ShouldBe(1);
                query.Load<Target>(target5.Id).Number.ShouldBe(1);
                query.Load<Target>(target6.Id).Number.ShouldBe(1);
            }
        }

        [Fact]
        public void duplicate_to_new_field()
        {
            #region sample_duplicate_to_new_field
            var target = Target.Random();
            target.AnotherString = null;
            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Duplicate(t => t.String, t => t.AnotherString);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var result = query.Load<Target>(target.Id);
                result.AnotherString.ShouldBe(target.String);
            }
            #endregion sample_duplicate_to_new_field
        }

        [Fact]
        public void duplicate_to_multiple_new_fields()
        {
            var target = Target.Random();
            target.StringField = null;
            target.Inner = null;
            theSession.Store(target);
            theSession.SaveChanges();

            #region sample_duplicate_to_multiple_new_fields
            theSession.Patch<Target>(target.Id).Duplicate(t => t.String,
                t => t.StringField,
                t => t.Inner.String,
                t => t.Inner.AnotherString);
            #endregion sample_duplicate_to_multiple_new_fields
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var result = query.Load<Target>(target.Id);

                result.StringField.ShouldBe(target.String);
                SpecificationExtensions.ShouldNotBeNull(result.Inner);
                result.Inner.String.ShouldBe(target.String);
                result.Inner.AnotherString.ShouldBe(target.String);
            }
        }

        #region sample_increment_for_int
        [Fact]
        public void increment_for_int()
        {
            var target = Target.Random();
            target.Number = 6;

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Increment(x => x.Number);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).Number.ShouldBe(7);
            }
        }

        #endregion sample_increment_for_int

        #region sample_increment_for_int_with_explicit_increment
        [Fact]
        public void increment_for_int_with_explicit_increment()
        {
            var target = Target.Random();
            target.Number = 6;

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Increment(x => x.Number, 3);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).Number.ShouldBe(9);
            }
        }

        #endregion sample_increment_for_int_with_explicit_increment

        [Fact]
        public void increment_for_long()
        {
            var target = Target.Random();
            target.Long = 13;

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Increment(x => x.Long);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).Long.ShouldBe(14);
            }
        }

        [Fact]
        public void increment_for_double()
        {
            var target = Target.Random();
            target.Double = 11.2;

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Increment(x => x.Double, 2.4);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).Double.ShouldBe(13.6);
            }
        }

        [Fact]
        public void increment_for_float()
        {
            var target = Target.Random();
            target.Float = 11.2F;

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Increment(x => x.Float, 2.4F);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).Float.ShouldBe(13.6F);
            }
        }

        [Fact]
        public void append_to_a_primitive_array()
        {
            var target = Target.Random();
            target.NumberArray = new[] { 1, 2, 3 };

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Append(x => x.NumberArray, 4);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).NumberArray
                    .ShouldHaveTheSameElementsAs(1, 2, 3, 4);
            }
        }

        [Fact]
        public void append_if_not_exists_to_a_primitive_array()
        {
            var target = Target.Random();
            target.NumberArray = new[] { 1, 2, 3 };

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).AppendIfNotExists(x => x.NumberArray, 3);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).NumberArray
                    .ShouldHaveTheSameElementsAs(1, 2, 3);
            }

            theSession.Patch<Target>(target.Id).AppendIfNotExists(x => x.NumberArray, 4);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).NumberArray
                    .ShouldHaveTheSameElementsAs(1, 2, 3, 4);
            }
        }

        #region sample_append_complex_element
        [Fact]
        public void append_complex_element()
        {
            var target = Target.Random(true);
            var initialCount = target.Children.Length;

            var child = Target.Random();

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Append(x => x.Children, child);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var target2 = query.Load<Target>(target.Id);
                target2.Children.Length.ShouldBe(initialCount + 1);

                target2.Children.Last().Id.ShouldBe(child.Id);
            }
        }

        #endregion sample_append_complex_element

        [Fact]
        public void append_if_not_exists_complex_element()
        {
            var target = Target.Random(true);
            var initialCount = target.Children.Length;

            var child = Target.Random();
            var child2 = Target.Random();

            theSession.Store(target);
            theSession.SaveChanges();
            theSession.Patch<Target>(target.Id).Append(x => x.Children, child);
            theSession.SaveChanges();
            theSession.Patch<Target>(target.Id).AppendIfNotExists(x => x.Children, child);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var target2 = query.Load<Target>(target.Id);
                target2.Children.Length.ShouldBe(initialCount + 1);

                target2.Children.Last().Id.ShouldBe(child.Id);
            }

            theSession.Patch<Target>(target.Id).AppendIfNotExists(x => x.Children, child2);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var target2 = query.Load<Target>(target.Id);
                target2.Children.Length.ShouldBe(initialCount + 2);

                target2.Children.Last().Id.ShouldBe(child2.Id);
            }
        }

        [Fact]
        public void insert_first_to_a_primitive_array()
        {
            var target = Target.Random();
            target.NumberArray = new[] { 1, 2, 3 };

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Insert(x => x.NumberArray, 4);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).NumberArray
                    .ShouldHaveTheSameElementsAs(4, 1, 2, 3);
            }
        }

        [Fact]
        public void insert_if_not_exists_first_to_a_primitive_array()
        {
            var target = Target.Random();
            target.NumberArray = new[] { 1, 2, 3 };

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).InsertIfNotExists(x => x.NumberArray, 1);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).NumberArray
                    .ShouldHaveTheSameElementsAs(1, 2, 3);
            }

            theSession.Patch<Target>(target.Id).InsertIfNotExists(x => x.NumberArray, 4);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).NumberArray
                    .ShouldHaveTheSameElementsAs(4, 1, 2, 3);
            }
        }

        [Fact]
        public void insert_first_to_a_primitive_array_at_a_certain_position()
        {
            var target = Target.Random();
            target.NumberArray = new[] { 1, 2, 3 };

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Insert(x => x.NumberArray, 4, 2);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).NumberArray
                    .ShouldHaveTheSameElementsAs(1, 2, 4, 3);
            }
        }

        [Fact]
        public void insert_if_not_exists_first_to_a_primitive_array_at_a_certain_position()
        {
            var target = Target.Random();
            target.NumberArray = new[] { 1, 2, 3 };

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).InsertIfNotExists(x => x.NumberArray, 3, 2);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).NumberArray
                    .ShouldHaveTheSameElementsAs(1, 2, 3);
            }

            theSession.Patch<Target>(target.Id).InsertIfNotExists(x => x.NumberArray, 4, 2);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Load<Target>(target.Id).NumberArray
                    .ShouldHaveTheSameElementsAs(1, 2, 4, 3);
            }
        }

        #region sample_insert_first_complex_element
        [Fact]
        public void insert_first_complex_element()
        {
            var target = Target.Random(true);
            var initialCount = target.Children.Length;

            var child = Target.Random();

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Insert(x => x.Children, child);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var target2 = query.Load<Target>(target.Id);
                target2.Children.Length.ShouldBe(initialCount + 1);

                target2.Children.First().Id.ShouldBe(child.Id);
            }
        }

        #endregion sample_insert_first_complex_element

        [Fact]
        public void insert_if_not_exists_first_complex_element()
        {
            var target = Target.Random(true);
            var initialCount = target.Children.Length;

            var child = Target.Random();
            var child2 = Target.Random();
            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Insert(x => x.Children, child);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var target2 = query.Load<Target>(target.Id);
                target2.Children.Length.ShouldBe(initialCount + 1);

                target2.Children.First().Id.ShouldBe(child.Id);
            }

            theSession.Patch<Target>(target.Id).InsertIfNotExists(x => x.Children, child);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var target2 = query.Load<Target>(target.Id);
                target2.Children.Length.ShouldBe(initialCount + 1);

                target2.Children.First().Id.ShouldBe(child.Id);
            }

            theSession.Patch<Target>(target.Id).InsertIfNotExists(x => x.Children, child2);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var target2 = query.Load<Target>(target.Id);
                target2.Children.Length.ShouldBe(initialCount + 2);

                target2.Children.First().Id.ShouldBe(child2.Id);
            }
        }

        [Fact]
        public void rename_shallow_prop()
        {
            var target = Target.Random(true);
            target.String = "Foo";
            target.AnotherString = "Bar";

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Rename("String", x => x.AnotherString);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var target2 = query.Load<Target>(target.Id);
                target2.AnotherString.ShouldBe("Foo");
                SpecificationExtensions.ShouldBeNull(target2.String);
            }
        }

        #region sample_rename_deep_prop
        [Fact]
        public void rename_deep_prop()
        {
            var target = Target.Random(true);
            target.Inner.String = "Foo";
            target.Inner.AnotherString = "Bar";

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Rename("String", x => x.Inner.AnotherString);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var target2 = query.Load<Target>(target.Id);
                target2.Inner.AnotherString.ShouldBe("Foo");
                SpecificationExtensions.ShouldBeNull(target2.Inner.String);
            }
        }

        #endregion sample_rename_deep_prop

        #region sample_remove_primitive_element
        [Fact]
        public void remove_primitive_element()
        {
            var target = Target.Random();
            var initialCount = target.NumberArray.Length;

            var random = new Random();
            var child = target.NumberArray[random.Next(0, initialCount)];

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Remove(x => x.NumberArray, child);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var target2 = query.Load<Target>(target.Id);
                target2.NumberArray.Length.ShouldBe(initialCount - 1);

                target2.NumberArray.ShouldHaveTheSameElementsAs(target.NumberArray.ExceptFirst(child));
            }
        }

        #endregion sample_remove_primitive_element

        #region sample_remove_repeated_primitive_element
        [Fact]
        public void remove_repeated_primitive_elements()
        {
            var target = Target.Random();
            var initialCount = target.NumberArray.Length;

            var random = new Random();
            var child = target.NumberArray[random.Next(0, initialCount)];
            var occurances = target.NumberArray.Count(e => e == child);
            if (occurances < 2)
            {
                target.NumberArray = target.NumberArray.Concat(new[] { child }).ToArray();
                ++occurances;
                ++initialCount;
            }

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Remove(x => x.NumberArray, child, RemoveAction.RemoveAll);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var target2 = query.Load<Target>(target.Id);
                target2.NumberArray.Length.ShouldBe(initialCount - occurances);

                target2.NumberArray.ShouldHaveTheSameElementsAs(target.NumberArray.Except(new[] { child }));
            }
        }

        #endregion sample_remove_repeated_primitive_element

        #region sample_remove_complex_element
        [Fact]
        public void remove_complex_element()
        {
            var target = Target.Random(true);
            var initialCount = target.Children.Length;

            var random = new Random();
            var child = target.Children[random.Next(0, initialCount)];

            theSession.Store(target);
            theSession.SaveChanges();

            theSession.Patch<Target>(target.Id).Remove(x => x.Children, child);
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var target2 = query.Load<Target>(target.Id);
                target2.Children.Length.ShouldBe(initialCount - 1);

                target2.Children.ShouldNotContain(t => t.Id == child.Id);
            }
        }

        #endregion sample_remove_complex_element

        [Fact]
        public void delete_redundant_property()
        {
            var target = Target.Random();
            theSession.Store(target);
            theSession.SaveChanges();

            #region sample_delete_redundant_property
            theSession.Patch<Target>(target.Id).Delete("String");
            #endregion sample_delete_redundant_property
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var result = query.Load<Target>(target.Id);

                SpecificationExtensions.ShouldBeNull(result.String);
            }
        }

        [Fact]
        public void delete_redundant_nested_property()
        {
            var target = Target.Random(true);
            theSession.Store(target);
            theSession.SaveChanges();

            #region sample_delete_redundant_nested_property
            theSession.Patch<Target>(target.Id).Delete("String", t => t.Inner);
            #endregion sample_delete_redundant_nested_property
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var result = query.Load<Target>(target.Id);

                SpecificationExtensions.ShouldBeNull(result.Inner.String);
            }
        }

        [Fact]
        public void delete_existing_property()
        {
            var target = Target.Random(true);
            theSession.Store(target);
            theSession.SaveChanges();

            #region sample_delete_existing_property
            theSession.Patch<Target>(target.Id).Delete(t => t.Inner);
            #endregion sample_delete_existing_property
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                var result = query.Load<Target>(target.Id);

                SpecificationExtensions.ShouldBeNull(result.Inner);
            }
        }

        [Fact]
        public void delete_property_from_many_documents()
        {
            for (var i = 0; i < 15; i++)
            {
                theSession.Store(Target.Random());
            }
            theSession.SaveChanges();

            #region sample_delete_property_from_many_documents
            const string where = "(data ->> 'String') is not null";
            theSession.Query<Target>(where).Count.ShouldBe(15);
            theSession.Patch<Target>(new WhereFragment(where)).Delete("String");
            theSession.SaveChanges();

            using (var query = theStore.QuerySession())
            {
                query.Query<Target>(where).Count(t => t.String != null).ShouldBe(0);
            }
            #endregion sample_delete_property_from_many_documents
        }

        [Fact]
        public async Task bug_611_duplicate_field_is_updated_by_set_operation()
        {
            var mapping = theStore.Storage.MappingFor(typeof(Target));
            var field = mapping.DuplicateField("String");
            await theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            var entity = Target.Random();
            theSession.Store(entity);
            await theSession.SaveChangesAsync();

            var newval = new string(entity.String.Reverse().ToArray());
            theSession.Patch<Target>(entity.Id).Set(t => t.String, newval);
            await theSession.SaveChangesAsync();

            using var command = theSession.Connection.CreateCommand();
            command.CommandText = $"select count(*) from {mapping.TableName.QualifiedName} " +
                                  $"where data->>'String' = '{newval}' and {field.ColumnName} = '{newval}'";
            var count = (long)(command.ExecuteScalar() ?? 0);
            count.ShouldBe(1);
        }

        [Fact]
        public async Task bug_611_duplicate_field_is_updated_by_set_operation_with_multiple_duplicates_smoke_test()
        {
            var mapping = theStore.Storage.MappingFor(typeof(Target));
            var field = mapping.DuplicateField("String");
            var field2 = mapping.DuplicateField(nameof(Target.Number));
            await theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            var entity = Target.Random();
            theSession.Store(entity);
            await theSession.SaveChangesAsync();

            var newval = new string(entity.String.Reverse().ToArray());
            theSession.Patch<Target>(entity.Id).Set(t => t.String, newval);
            await theSession.SaveChangesAsync();

            using var command = theSession.Connection.CreateCommand();
            command.CommandText = $"select count(*) from {mapping.TableName.QualifiedName} " +
                                  $"where data->>'String' = '{newval}' and {field.ColumnName} = '{newval}'";
            var count = (long)(command.ExecuteScalar() ?? 0);
            count.ShouldBe(1);
        }

        [Theory]
        [InlineData(TenancyStyle.Single)]
        [InlineData(TenancyStyle.Conjoined)]
        public async Task patch_inside_inline_projection_does_not_error_during_savechanges(TenancyStyle tenancyStyle)
        {
            StoreOptions(_ =>
            {
                _.AutoCreateSchemaObjects = AutoCreate.All;
                _.Events.TenancyStyle = tenancyStyle;

                _.Projections.Add(new QuestPatchTestProjection());

                _.UseJavascriptTransformsAndPatching();
            });

            await theStore.Schema.ApplyAllConfiguredChangesToDatabase();

            var aggregateId = Guid.NewGuid();
            var quest = new Quest
            {
                Id = aggregateId,
            };
            var questStarted = new QuestStarted
            {
                Id = aggregateId,
                Name = "New Quest",
            };

            theSession.Events.Append(aggregateId, quest, questStarted);
            await theSession.SaveChangesAsync();

            (await theSession.Events.FetchStreamStateAsync(aggregateId)).Version.ShouldBe(2);
        }

        public class QuestPatchTestProjection: IProjection
        {
            public Guid Id { get; set; }

            public string Name { get; set; }

            public void Apply(IDocumentOperations operations, IReadOnlyList<StreamAction> streams)
            {
                var questEvents = streams.SelectMany(x => x.Events).OrderBy(s => s.Sequence).Select(s => s.Data);

                foreach (var @event in questEvents)
                {
                    if (@event is Quest quest)
                    {
                        operations.Store(new QuestPatchTestProjection { Id = quest.Id });
                    }
                    else if (@event is QuestStarted started)
                    {
                        operations.Patch<QuestPatchTestProjection>(started.Id).Set(x => x.Name, "New Name");
                    }
                }
            }

            public Task ApplyAsync(IDocumentOperations operations, IReadOnlyList<StreamAction> streams,
                CancellationToken cancellation)
            {
                return Task.CompletedTask;
            }
        }



    }

    internal static class EnumerableExtensions
    {
        public static IEnumerable<T> ExceptFirst<T>(this IEnumerable<T> enumerable, T item)
        {
            var encountered = false;
            var expected = new List<T>();
            foreach (var val in enumerable)
            {
                if (!encountered && val.Equals(item))
                {
                    encountered = true;
                    continue;
                }
                expected.Add(val);
            }
            return expected;
        }
    }
}
