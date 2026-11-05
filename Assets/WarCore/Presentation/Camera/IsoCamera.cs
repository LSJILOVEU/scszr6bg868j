using System;

namespace WarCore.Presentation
{
    // Where a screen point lands on the ground: metres for the picture, whole millimetres for the bridge (Sim recomputes Y itself).
    public readonly struct GroundPick
    {
        public float X { get; }
        public float Z { get; }
        public long XMm { get; }
        public long ZMm { get; }

        public GroundPick(float x, float z)
        {
            X = x; Z = z;
            XMm = (long)MathF.Round(x * 1000f);
            ZMm = (long)MathF.Round(z * 1000f);
        }
    }

    // Orthographic isometric camera, engine-free. Same convention as Unity's Quaternion.Euler(Pitch, Yaw, 0):
    // forward = (sinY*cosP, -sinP, cosY*cosP), right = (cosY, 0, -sinY), up = forward x right.
    // The ground is a flat plane for now; once terrain exists the pick should ask the map's ground query instead.
    public sealed class IsoCamera
    {
        public const float DefaultYaw = 45f;
        public const float DefaultPitch = 45f;
        public const float MinPitch = 20f;
        public const float MaxPitch = 80f;
        public const float MinZoom = 4f;
        public const float MaxZoom = 120f;
        // Orthographic: the distance changes no picture, only where the rays start. It must keep every ray's start above the
        // ground at any pitch and zoom (the bottom edge starts Zoom*cos(pitch) lower than the centre), so it grows with Zoom.
        private const float MinDistance = 100f, DistancePerZoom = 5f;

        private const float Rad = MathF.PI / 180f;

        public float FocusX { get; private set; }
        public float FocusZ { get; private set; }
        public float GroundY { get; }
        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        // Half the visible height in metres.
        public float Zoom { get; private set; }
        public float Distance => MathF.Max(MinDistance, DistancePerZoom * Zoom);

        public IsoCamera(float zoom, float groundY = 0f, float yaw = DefaultYaw, float pitch = DefaultPitch)
        {
            GroundY = groundY;
            Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
            Yaw = Wrap(yaw);
            Pitch = Math.Clamp(pitch, MinPitch, MaxPitch);
        }

        private static float Wrap(float degrees)
        {
            float wrapped = degrees % 360f;
            return wrapped < 0f ? wrapped + 360f : wrapped;
        }

        public void Follow(float x, float z) { FocusX = x; FocusZ = z; }
        public void Rotate(float degrees) => Yaw = Wrap(Yaw + degrees);
        public void Tilt(float degrees) => Pitch = Math.Clamp(Pitch + degrees, MinPitch, MaxPitch);
        public void ZoomBy(float factor) => Zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);

        public (float X, float Y, float Z) Forward
        {
            get
            {
                float sy = MathF.Sin(Yaw * Rad), cy = MathF.Cos(Yaw * Rad), sp = MathF.Sin(Pitch * Rad), cp = MathF.Cos(Pitch * Rad);
                return (sy * cp, -sp, cy * cp);
            }
        }

        public (float X, float Y, float Z) Right
        {
            get
            {
                float sy = MathF.Sin(Yaw * Rad), cy = MathF.Cos(Yaw * Rad);
                return (cy, 0f, -sy);
            }
        }

        public (float X, float Y, float Z) Up
        {
            get
            {
                float sy = MathF.Sin(Yaw * Rad), cy = MathF.Cos(Yaw * Rad), sp = MathF.Sin(Pitch * Rad), cp = MathF.Cos(Pitch * Rad);
                return (sy * sp, cp, cy * sp);
            }
        }

        // The point the camera looks at sits at the screen centre; the camera backs off along its forward axis.
        public (float X, float Y, float Z) Position
        {
            get
            {
                var f = Forward;
                return (FocusX - f.X * Distance, GroundY - f.Y * Distance, FocusZ - f.Z * Distance);
            }
        }

        // Screen pixels, origin bottom-left (Unity's Input.mousePosition).
        public GroundPick ScreenToGround(float screenX, float screenY, float screenWidth, float screenHeight)
        {
            float nx = 2f * screenX / screenWidth - 1f, ny = 2f * screenY / screenHeight - 1f;
            float aspect = screenWidth / screenHeight;
            var p = Position; var f = Forward; var r = Right; var u = Up;
            float ox = p.X + r.X * nx * Zoom * aspect + u.X * ny * Zoom;
            float oy = p.Y + r.Y * nx * Zoom * aspect + u.Y * ny * Zoom;
            float oz = p.Z + r.Z * nx * Zoom * aspect + u.Z * ny * Zoom;
            float t = (GroundY - oy) / f.Y;
            return new GroundPick(ox + f.X * t, oz + f.Z * t);
        }

        // Keys to a world-axis direction: up (iy=+1, W) is screen-up along the ground, right (ix=+1, D) is screen-right.
        // Length is at most 1, so a diagonal is not faster. This is what P1a hands to Sim.
        public (float X, float Z) MoveAxes(float ix, float iy)
        {
            float sy = MathF.Sin(Yaw * Rad), cy = MathF.Cos(Yaw * Rad);
            float x = iy * sy + ix * cy, z = iy * cy - ix * sy;
            float length = MathF.Sqrt(x * x + z * z);
            return length > 1f ? (x / length, z / length) : (x, z);
        }
    }
}
