using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity
{
    // Keys and mouse to IsoCamera, and IsoCamera to the Unity camera. The maths lives in IsoCamera (tested without Unity).
    // Key bindings are placeholders, all in this table. A Mac trackpad has no middle button, so Q/E and R/F do the same.
    public sealed class IsoCameraInput
    {
        public const KeyCode YawLeft = KeyCode.Q, YawRight = KeyCode.E, PitchUp = KeyCode.R, PitchDown = KeyCode.F;
        public const int DragButton = 2; // middle: horizontal drag turns, vertical drag tilts
        private const float KeyYawDegreesPerSecond = 90f, KeyPitchDegreesPerSecond = 45f, DragDegreesPerAxisUnit = 3f, ZoomPerNotch = 0.9f;

        public void Update(IsoCamera camera, float deltaTime)
        {
            if (Input.GetKey(YawLeft)) camera.Rotate(-KeyYawDegreesPerSecond * deltaTime);
            if (Input.GetKey(YawRight)) camera.Rotate(KeyYawDegreesPerSecond * deltaTime);
            if (Input.GetKey(PitchUp)) camera.Tilt(KeyPitchDegreesPerSecond * deltaTime);
            if (Input.GetKey(PitchDown)) camera.Tilt(-KeyPitchDegreesPerSecond * deltaTime);
            if (Input.GetMouseButton(DragButton))
            {
                camera.Rotate(Input.GetAxis("Mouse X") * DragDegreesPerAxisUnit);
                camera.Tilt(-Input.GetAxis("Mouse Y") * DragDegreesPerAxisUnit);
            }
            float scroll = Input.mouseScrollDelta.y;
            if (scroll != 0f) camera.ZoomBy(Mathf.Pow(ZoomPerNotch, scroll));
        }

        public static void Apply(IsoCamera camera, Camera target)
        {
            var position = camera.Position;
            target.orthographic = true;
            target.orthographicSize = camera.Zoom;
            target.nearClipPlane = 0.1f;
            target.farClipPlane = camera.Distance * 4f;
            target.transform.SetPositionAndRotation(new Vector3(position.X, position.Y, position.Z), Quaternion.Euler(camera.Pitch, camera.Yaw, 0f));
        }
    }
}
