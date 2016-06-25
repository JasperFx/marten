using System;
using Baseline;
using Octokit;

namespace CodeTracker
{
    public static class BigBangRecorder
    {
        public static void TryIt()
        {
            var directory = AppDomain.CurrentDomain.BaseDirectory.ParentDirectory().ParentDirectory();

            var recorder = new GithubDataRecorder(new Credentials("YOURUSERNAME", "YOURPASSWORD"), directory);


            //recorder.RecordProject("JasperFx", "alba").Wait();
            //recorder.RecordProject("JasperFx", "marten").Wait();
            //recorder.RecordProject("JasperFx", "baseline").Wait();
            //recorder.RecordProject("DarthFubuMVC", "fubucore").Wait();
            //recorder.RecordProject("DarthFubuMVC", "bottles").Wait();
            //recorder.RecordProject("DarthFubuMVC", "fubuvalidation").Wait();
            recorder.RecordProject("structuremap", "structuremap").Wait();
            recorder.RecordProject("structuremap", "structuremap.dnx").Wait();
        }
    }
}