using System;

namespace WarCore.Presentation
{
    public sealed class PlayerInputMapper
    {
        private bool hasAim;
        private GroundPick lastAim;

        // Screen origin is bottom-left. Outside the window or with a nonpositive viewport,
        // retain the last valid pick. Before the first valid pick, use the current focus's
        // ground point (without storing it as a valid mouse pick).
        public PlayerInput Map(IsoCamera camera, float ix, float iy, float x, float y,
            float w, float h, bool mouseInWindow, bool primary)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (mouseInWindow && w > 0 && h > 0)
            {
                lastAim = camera.ScreenToGround(x, y, w, h);
                hasAim = true;
            }
            GroundPick aim = hasAim ? lastAim : new GroundPick(camera.FocusX, camera.FocusZ);
            var move = camera.MoveAxes(ix, iy);
            return new PlayerInput(Milli(move.X), Milli(move.Z), aim.XMm, aim.ZMm, primary);
        }

        // Scale then truncate toward zero. Clamp before the cast to keep the conversion bounded.
        private static int Milli(float axis) => (int)Math.Clamp(axis * 1000f, -1000f, 1000f);
    }
}
