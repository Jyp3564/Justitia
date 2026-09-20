using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

namespace Justitia
{
    [DefaultExecutionOrder(100)]
    public sealed class MouseSeatView : MonoBehaviour
    {
        public SeatPlayer Guest;
        public SeatPlayer Host;
        public Camera ViewCamera;
        public TMP_Text StatusLabel;
        public float Sensitivity = 0.12f;
        public bool UiInputActive;
        public bool CinematicLocked;
        private SeatPlayer current;
        private float yaw, pitch;
        private bool looking;
        private Vector3 overviewPosition;
        private Quaternion overviewRotation;

        private void Awake()
        {
            overviewPosition = ViewCamera.transform.position;
            overviewRotation = ViewCamera.transform.rotation;
            foreach (var label in GetComponentsInChildren<TMP_Text>(true))
                if (label.text.StartsWith("RIGHT DRAG:"))
                    label.text = "MOVE MOUSE: LOOK  /  NO MOVEMENT OR JUMP";
            ShowOverview();
        }

        public void ShowGuest() => Select(Guest);
        public void ShowHost() => Select(Host);
        public void ResetView() { yaw = pitch = 0; ReleaseMouse(); }

        private void Select(SeatPlayer player)
        {
            if (CinematicLocked) return;
            current = player;
            ResetView();
            Guest.SetLocalView(current == Guest);
            Host.SetLocalView(current == Host);
            if (StatusLabel) StatusLabel.text = player.Role + " VIEW  |  Move mouse to look";
        }

        public void ShowOverview()
        {
            if (CinematicLocked) return;
            current = null;
            ResetView();
            Guest.SetLocalView(false);
            Host.SetLocalView(false);
            ViewCamera.transform.SetPositionAndRotation(overviewPosition, overviewRotation);
            if (StatusLabel) StatusLabel.text = "CHARACTER PREVIEW  |  Choose Guest or Host";
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null || !current || UiInputActive || CinematicLocked || !Application.isFocused)
            { ReleaseMouse(); return; }
            if (!looking || Cursor.lockState != CursorLockMode.Locked)
            {
                looking = true;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                return;
            }
            ApplyLookDelta(mouse.delta.ReadValue());
        }

        public void ApplyLookDelta(Vector2 delta)
        {
            if (!current || UiInputActive || CinematicLocked) return;
            yaw = Mathf.Clamp(yaw + delta.x * Sensitivity, -100, 100);
            pitch = Mathf.Clamp(pitch - delta.y * Sensitivity, -60, 60);
        }

        private void LateUpdate()
        {
            if (!current || CinematicLocked) return;
            ViewCamera.transform.SetPositionAndRotation(current.Eye.position,
                current.SeatAnchor.rotation * Quaternion.Euler(pitch, yaw, 0));
        }

        private void ReleaseMouse()
        {
            looking = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnApplicationFocus(bool focused) { if (!focused) ReleaseMouse(); }
        private void OnDisable()
        {
            ReleaseMouse();
            if (Guest) Guest.SetLocalView(false);
            if (Host) Host.SetLocalView(false);
        }
    }
}
