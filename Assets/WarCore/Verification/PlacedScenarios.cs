using System;
using System.Globalization;
using WarCore.Sim;
using WarCore.Verification.Fixtures;
using Simulation = WarCore.Sim.Sim;
namespace WarCore.Verification
{
    internal static class PlacedScenarios
    {
        internal static void Run(Report report)
        {
            var rows=VectorJson.Parse(VectorData3.Text)["cases"];
            for(int row=0;row<rows.Count;row++)
            {
                var item=rows[row];var entries=new PlacedCatalogEntry[item["entries"].Count];
                for(int i=0;i<entries.Length;i++)entries[i]=new PlacedCatalogEntry((ulong)item["entries"][i][0].Integer,(ulong)item["entries"][i][1].Integer,(byte)item["entries"][i][2].Integer);
                var catalog=new PlacedCatalog(new StableContentManifest(0),entries);
                var destroyed=new int[item["destroyed"].Count];for(int i=0;i<destroyed.Length;i++)destroyed[i]=(int)item["destroyed"][i].Integer;
                var gens=new PlacedGeneration[item["generations"].Count];for(int i=0;i<gens.Length;i++)gens[i]=new PlacedGeneration((int)item["generations"][i][0].Integer,(ushort)item["generations"][i][1].Integer);
                var sim=Simulation.CreateRetainedWorld(123,catalog,initial:new InitialPlacedState(catalog.Digest,destroyed,gens));
                string actual=sim.StateHashRetained().ToString("X16");
                HashScenarios.Require(actual==item["hash"].Text,"Placed hash differs from independent oracle.");
                byte[] snapshot=sim.SaveRetainedSnapshot();
                HashScenarios.Require(BitConverter.ToString(snapshot).Replace("-","").ToLowerInvariant()==item["snapshot"].Text,"Placed snapshot differs from independent oracle.");
                sim.RestoreRetainedSnapshot(snapshot,true);
                HashScenarios.Require(sim.StateHashRetained().ToString("X16")==actual,"Placed restored hash differs.");
                report.Line("placed.vector."+item["name"].Text,"hash="+actual+";snapshotBytes="+snapshot.Length+";restored=equal");
            }
            foreach(var data in new[]{M0c1Package.Ranged,M0c1Package.Melee})
            {
                var sim=M0c1Fixture.Create(out var system,32,package:data);system.Auto=true;sim.Entities.Create(M0c1Fixture.Parent);
                var resumed=M0c1Fixture.Create(out _,32,package:data);var digest=new StableDigest();
                for(int tick=0;tick<=300;tick++)
                {
                    byte[] snapshot=sim.SaveRetainedSnapshot();resumed.RestoreRetainedSnapshot(snapshot,true);
                    HashScenarios.Require(sim.StateHashRetained()==resumed.StateHashRetained(),"Placed checkpoint mismatch.");
                    sim.Step(default);resumed.Step(default);
                    HashScenarios.Require(sim.StateHashRetained()==resumed.StateHashRetained(),"Placed continuation mismatch.");
                    digest.Row(sim.StateHashRetained().ToString("X16"));
                }
                report.Digest("placed.replay."+data.Name,digest);
            }
        }
    }
}
