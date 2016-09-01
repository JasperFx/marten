using System.Threading.Tasks;
using Baseline;
using Xunit;

namespace Marten.Testing.Bugs
{
    public class Bug_505_contention_with_hilo : IntegratedFixture
    {
        //[Fact] -- don't run this as part of the build
        public void try_to_make_hilo_fail()
        {
            var tasks = new Task[50];
            for (var i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Factory.StartNew(() =>
                {
                    for (int j = 0; j < 50; j++)
                    {
                        using (var session = theStore.OpenSession())
                        {
                            for (int k = 0; k < 50; k++)
                            {
                                var doc = new IntDoc();
                                session.Store(doc);
                            }

                            session.SaveChanges();
                        }


                    }
                });
            }

            Task.WaitAll(tasks, 5.Minutes());
        }

        
    }
}