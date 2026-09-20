using UnityEngine;

namespace Justitia
{
    public enum PlayerRole { Guest, Host }
    public enum SeatId { Left, Right }

    public sealed class SeatPlayer : MonoBehaviour
    {
        public PlayerRole Role;
        public SeatId Seat;
        public Transform SeatAnchor;
        public Transform Eye;
        public Renderer[] Visuals;

        public void FollowSeat()
        {
            if (!SeatAnchor) return;
            transform.SetPositionAndRotation(SeatAnchor.position, SeatAnchor.rotation);
        }

        private void LateUpdate() => FollowSeat();

        public void SetLocalView(bool local)
        {
            foreach (var visual in Visuals)
                if (visual) visual.enabled = !local;
        }
    }
}
