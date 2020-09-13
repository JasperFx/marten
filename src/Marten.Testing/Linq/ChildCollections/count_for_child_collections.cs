using System;
using System.Linq;
using Marten.Services;
using Marten.Testing.Harness;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Marten.Testing.Linq.ChildCollections
{
    public class count_for_child_collections : IntegrationContext
    {
        private readonly ITestOutputHelper _output;

        [Fact]
        public void GivenTwoLevelsOfChildCollections_WhenCountCalled_ThenReturnsProperCount()
        {
            StoreOptions(op => op.UseDefaultSerialization(collectionStorage: CollectionStorage.AsArray));

            SetupTestData();

            theSession.Logger = new TestOutputMartenLogger(_output);

            var result = theSession
                .Query<Root>()
                .Where(r => r.ChildsLevel1.Count(c1 => c1.Name == "child-1.1") == 1)
                .ToList();

            result.ShouldHaveSingleItem();
        }

        private void SetupTestData()
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
            theSession.SaveChanges();
        }

        public count_for_child_collections(DefaultStoreFixture fixture, ITestOutputHelper output) : base(fixture)
        {
            _output = output;
        }
    }
}
