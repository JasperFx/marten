using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Schema;
using Marten.Schema.Identity.Sequences;
using Marten.Storage;
using Shouldly;
using StructureMap;
using Xunit;

namespace Marten.Testing.Schema.Identity.Sequences
{
    public class HiloSequenceTests
    {
        private readonly IContainer _container = Container.For<DevelopmentModeRegistry>();

        private readonly HiloSequence _theSequence;

        public HiloSequenceTests()
        {
            _container.GetInstance<DocumentCleaner>().CompletelyRemoveAll();

            var storeOptions = new StoreOptions();

            _container.GetInstance<ITenant>().EnsureStorageExists(typeof(SequenceFactory));
            
            _theSequence = new HiloSequence(new ConnectionSource(), storeOptions, "foo", new HiloSettings());
        }

        [Fact]
        public void default_values()
        {
            _theSequence.CurrentHi.ShouldBe(-1);
            _theSequence.MaxLo.ShouldBe(1000);
        }

        [Fact]
        public void should_advance_initial_case()
        {
            _theSequence.ShouldAdvanceHi().ShouldBeTrue();
        }

        [Fact]
        public void advance_to_next_hi_from_initial_state()
        {
            _theSequence.AdvanceToNextHi();

            _theSequence.CurrentLo.ShouldBe(1);
            _theSequence.CurrentHi.ShouldBe(0);
        }

        [Fact]
        public void advance_to_next_hi_several_times()
        {
            _theSequence.AdvanceToNextHi();

            _theSequence.AdvanceToNextHi();
            _theSequence.CurrentHi.ShouldBe(1);

            _theSequence.AdvanceToNextHi();
            _theSequence.CurrentHi.ShouldBe(2);

            _theSequence.AdvanceToNextHi();
            _theSequence.CurrentHi.ShouldBe(3);
        }

        [Fact]
        public void advance_value_from_initial_state()
        {
            // Gotta do this at least once
            _theSequence.AdvanceToNextHi();

            _theSequence.AdvanceValue().ShouldBe(1);
            _theSequence.AdvanceValue().ShouldBe(2);
            _theSequence.AdvanceValue().ShouldBe(3);
            _theSequence.AdvanceValue().ShouldBe(4);
            _theSequence.AdvanceValue().ShouldBe(5);
        }

        [Fact]
        public void read_from_a_single_thread_from_0_to_5000()
        {
            for (var i = 0; i < 5000; i++)
            {
                _theSequence.NextLong().ShouldBe(i + 1);
            }
        }

        private Task<List<int>> startThread()
        {
            return Task.Factory.StartNew(() =>
            {
                var list = new List<int>();

                for (int i = 0; i < 1000; i++)
                {
                    list.Add(_theSequence.NextInt());
                }

                return list;
            });
        }

        [Fact]
        public void is_thread_safe()
        {
            var tasks = new Task<List<int>>[] {startThread(), startThread(), startThread(), startThread(), startThread(), startThread()};

            Task.WaitAll(tasks);

            var all = tasks.SelectMany(x => x.Result).ToArray();

            all.GroupBy(x => x).Any(x => x.Count() > 1).ShouldBeFalse();

            all.Distinct().Count().ShouldBe(tasks.Length * 1000);
        }
    }
}