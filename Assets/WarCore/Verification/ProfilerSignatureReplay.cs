using System;
using WarCore.Sim;
using WarCore.Verification.Fixtures;

namespace WarCore.Verification
{
    internal sealed class ProfilerSignatureReplay
    {
        private readonly SimBenchScene scene = new SimBenchScene(2000, SimBenchWorkload.Heavy);
        private readonly EntityId[][] entities;
        private readonly RayHit[][] rays;
        private long calls;
        private ulong checksum;
        internal int CallsPerFrame => scene.Queries.Length;

        internal ProfilerSignatureReplay()
        {
            entities = new EntityId[CallsPerFrame][];
            rays = new RayHit[CallsPerFrame][];
            for (int i = 0; i < CallsPerFrame; i++)
            {
                int count = scene.Queries[i].Run(scene.Grid, scene.Entities, scene.Rays);
                HashScenarios.Require(count == scene.Counts[i] && scene.Signature(scene.Queries[i].Kind, count) == scene.Signatures[i], "Signature capture mismatch.");
                if (scene.Queries[i].Kind == 0)
                {
                    rays[i] = new RayHit[count];
                    Array.Copy(scene.Rays, rays[i], count);
                }
                else
                {
                    entities[i] = new EntityId[count];
                    Array.Copy(scene.Entities, entities[i], count);
                }
            }
        }

        internal void Step()
        {
            for (int i = 0; i < CallsPerFrame; i++)
            {
                int count = scene.Counts[i];
                // Restore captured outputs into existing buffers. No query and no allocation.
                if (scene.Queries[i].Kind == 0) Array.Copy(rays[i], scene.Rays, count);
                else Array.Copy(entities[i], scene.Entities, count);
                ulong signature = scene.Signature(scene.Queries[i].Kind, count);
                HashScenarios.Require(signature == scene.Signatures[i], "Replayed signature mismatch.");
                checksum = unchecked(checksum * 1099511628211UL ^ signature);
                calls++;
            }
        }

        internal void Verify(int frames)
        {
            ulong expected = 0;
            for (int frame = 0; frame < frames; frame++)
                for (int i = 0; i < CallsPerFrame; i++) expected = unchecked(expected * 1099511628211UL ^ scene.Signatures[i]);
            HashScenarios.Require(calls == (long)frames * CallsPerFrame && checksum == expected && scene.Sim.Clock.Tick == 0, "Legacy signature replay work mismatch.");
        }
    }
}
