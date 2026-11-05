using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    [DefaultExecutionOrder(-100)]
    public sealed class SimHost : MonoBehaviour
    {
        private SimSession session;
        private EntityViews views;

        private void Awake()
        {
            session = SimSession.Create(new WorldRecipe(41, MoverKind.Interim,
                new[] { new UnitSpawn(1, 0, 1, 0, 0, 3000) },
                new[] { BridgeCommand.MoveTo(1, 1, 10000, 0), BridgeCommand.MoveTo(150, 1, 10000, 10000) }));
            views = new EntityViews(transform);
            views.Apply(session.Previous, session.Current, session.Alpha);
            Debug.Log("M1 sim started");
        }

        private void Update()
        {
            if (session.Tick >= 600)
                return;
            if (session.Tick < 600 - SimSession.MaxTicksPerFrame)
                session.Advance((long)(Time.unscaledDeltaTime * 1e6));
            else
                while (session.Tick < 600)
                    session.StepOnce();
            views.Apply(session.Previous, session.Current, session.Alpha);
        }

        private void OnGUI()
        {
            if (session != null)
                GUI.Box(new Rect(12, 12, 280, 52), $"Tick {session.Tick} / 600\nStateHash {session.StateHash:X16}");
        }
    }
}
