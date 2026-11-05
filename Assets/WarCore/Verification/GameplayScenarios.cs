using System;
using WarCore.Verification.Fixtures;
namespace WarCore.Verification
{
    internal static class GameplayScenarios
    {
        internal static void Run(Report report)
        {
            var cases=VectorJson.Parse(VectorData4.Text)["cases"];
            report.Line("source.gameplay","sha256="+VectorData4.Sha256);
            for(int i=0;i<cases.Count;i++)
            {
                var row=cases[i]; var sim=M0c2Fixture.Create(Convert.ToUInt64(row["rules"].Text,16));
                string hash=sim.StateHashRetained().ToString("X16"); byte[] snapshot=sim.SaveRetainedSnapshot();
                HashScenarios.Require(hash==row["hash"].Text,"Empty gameplay hash oracle");
                HashScenarios.Require(BitConverter.ToString(snapshot).Replace("-","").ToLowerInvariant()==row["snapshot"].Text,"Empty gameplay snapshot oracle");
                sim.RestoreRetainedSnapshot(snapshot,true);
                HashScenarios.Require(sim.StateHashRetained().ToString("X16")==hash,"Empty gameplay restore");
                report.Line("gameplay.vector."+row["name"].Text,"hash="+hash+";snapshotBytes="+snapshot.Length+";restored=equal");
            }
            RunAbilities(report,false);RunAbilities(report,true);RunLife(report,false);RunLife(report,true);
        }
        private static void RunLife(Report report,bool packageB)
        {
            var sim=M0c2S4Fixture.Create(packageB);var digest=new StableDigest();
            for(int tick=0;tick<6;tick++)digest.Row(M0c2S4Fixture.Replay(sim).ToString("X16"));
            byte[] snapshot=sim.SaveRetainedSnapshot();var restored=M0c2S4Fixture.Create(packageB,true);
            restored.RestoreRetainedSnapshot(snapshot,true);
            HashScenarios.Require(sim.StateHashRetained()==restored.StateHashRetained(),"S4 life restore");
            for(int tick=0;tick<106;tick++)
            {ulong expected=M0c2S4Fixture.Replay(sim),actual=M0c2S4Fixture.Replay(restored);
                HashScenarios.Require(expected==actual,"S4 continued life hash");digest.Row(expected.ToString("X16"));}
            var bytes=new StableDigest();bytes.Row(Convert.ToBase64String(snapshot));string name=packageB?"b":"a";
            report.Digest("gameplay.s4.replay."+name,digest);
            report.Line("gameplay.s4.snapshot."+name,"bytes="+snapshot.Length+";fnv1a64="+bytes.Hex+";restored=equal");
        }
        private static void RunAbilities(Report report,bool packageB)
        {
            var sim=M0c2S3Fixture.Create(packageB);var digest=new StableDigest();
            for(int tick=0;tick<38;tick++)digest.Row(M0c2S3Fixture.Step(sim).ToString("X16"));
            byte[] snapshot=sim.SaveRetainedSnapshot();
            var restored=M0c2S3Fixture.Create(packageB,true,1);restored.RestoreRetainedSnapshot(snapshot,true);
            HashScenarios.Require(sim.StateHashRetained()==restored.StateHashRetained(),"S3 windup restore");
            for(int tick=0;tick<74;tick++)
            {
                ulong expected=M0c2S3Fixture.Step(sim),actual=M0c2S3Fixture.Step(restored);
                HashScenarios.Require(expected==actual,"S3 resumed tick hash");digest.Row(expected.ToString("X16"));
            }
            // Comparing the full snapshot digest also covers S3 row bytes across backends.
            var bytes=new StableDigest();bytes.Row(Convert.ToBase64String(snapshot));
            string name=packageB?"b":"a";
            report.Digest("gameplay.s3.replay."+name,digest);
            report.Line("gameplay.s3.snapshot."+name,"bytes="+snapshot.Length+";fnv1a64="+bytes.Hex+";restored=equal");
        }
    }
}
