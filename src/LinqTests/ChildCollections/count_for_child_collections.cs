using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Marten;
using Marten.Linq;
using Marten.Testing.Harness;
using Shouldly;
using Xunit.Abstractions;

namespace LinqTests.ChildCollections;

public class count_for_child_collections : OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    [Fact]
    public async Task GivenTwoLevelsOfChildCollections_WhenCountCalled_ThenReturnsProperCount()
    {
        StoreOptions(op => op.UseDefaultSerialization(collectionStorage: CollectionStorage.AsArray));

        await SetupTestData();

        theSession.Logger = new TestOutputMartenLogger(_output);

        var result = theSession
            .Query<Root>()
            .Where(r => r.ChildsLevel1.Count(c1 => c1.Name == "child-1.1") == 1)
            .ToList();

        result.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task two_levels_of_child_collections_in_compiled_query()
    {
        StoreOptions(op => op.UseDefaultSerialization(collectionStorage: CollectionStorage.AsArray));

        await SetupTestData();

        theSession.Logger = new TestOutputMartenLogger(_output);

        var result = await theSession
            .Query<Root>()
            .Where(r => r.ChildsLevel1.Count(c1 => c1.Name == "child-1.1") == 1)
            .ToListAsync();

        var result2 = await theSession.QueryAsync(new ChildCollectionCountQuery());

        result.Single().Id.ShouldBe(result2.Single().Id);
    }

    public class ChildCollectionCountQuery: ICompiledListQuery<Root>
    {
        public Expression<Func<IMartenQueryable<Root>, IEnumerable<Root>>> QueryIs()
        {
            return q => q.Where(r => r.ChildsLevel1.Count(c1 => c1.Name == Name) == 1);
        }

        public string Name { get; set; } = "child-1.1";

    }

    private async Task SetupTestData()
    {
        var product1 = new Root
        {
            Id = Guid.NewGuid(),
            Name = "root-1",
            ChildsLevel1 = new[]{
                new ChildLevel1{
                    Id = Guid.NewGuid(),
                    Name = "child-1.1",
                    ChildsLevel2 = new [] {
                        new ChildLevel2{
                            Id = Guid.NewGuid(),
                            Name = "child-1.1.1"
                        },
                        new ChildLevel2{
                            Id = Guid.NewGuid(),
                            Name = "child-1.1.2"
                        }
                    }
                },
                new ChildLevel1{
                    Id = Guid.NewGuid(),
                    Name = "child-1.2",
                    ChildsLevel2 = new [] {
                        new ChildLevel2{
                            Id = Guid.NewGuid(),
                            Name = "child-1.2.1"
                        },
                        new ChildLevel2{
                            Id = Guid.NewGuid(),
                            Name = "child-1.2.2"
                        }
                    }
                }
            }
        };

        var product2 = new Root
        {
            Id = Guid.NewGuid(),
            Name = "root-2",
            ChildsLevel1 = new[]{
                new ChildLevel1{
                    Id = Guid.NewGuid(),
                    Name = "child-2.1",
                    ChildsLevel2 = new [] {
                        new ChildLevel2{
                            Id = Guid.NewGuid(),
                            Name = "child-2.1.1"
                        },
                        new ChildLevel2{
                            Id = Guid.NewGuid(),
                            Name = "child-2.1.2"
                        }
                    }
                },
                new ChildLevel1{
                    Id = Guid.NewGuid(),
                    Name = "child-2.2",
                    ChildsLevel2 = new [] {
                        new ChildLevel2{
                            Id = Guid.NewGuid(),
                            Name = "child-2.2.1"
                        },
                        new ChildLevel2{
                            Id = Guid.NewGuid(),
                            Name = "child-2.2.2"
                        }
                    }
                }
            }
        };

        theSession.Store(product1);
        theSession.Store(product2);
        await theSession.SaveChangesAsync();
    }

    public count_for_child_collections(ITestOutputHelper output)
    {
        _output = output;
    }
}
