using UnityEngine;

namespace DefenseGame.Combat
{
    public class EnemyPath : MonoBehaviour
    {
        [SerializeField] private Transform[] waypoints;

        public int WaypointCount => waypoints == null ? 0 : waypoints.Length;

        public Vector3 GetWaypointPosition(int index)
        {
            if (waypoints == null || index < 0 || index >= waypoints.Length || waypoints[index] == null)
            {
                return transform.position;
            }

            return waypoints[index].position;
        }

        private void OnDrawGizmos()
        {
            if (waypoints == null || waypoints.Length == 0)
            {
                return;
            }

            Gizmos.color = Color.yellow;

            for (int i = 0; i < waypoints.Length; i++)
            {
                Transform waypoint = waypoints[i];
                if (waypoint == null)
                {
                    continue;
                }

                Gizmos.DrawSphere(waypoint.position, 0.15f);

                if (i + 1 < waypoints.Length && waypoints[i + 1] != null)
                {
                    Gizmos.DrawLine(waypoint.position, waypoints[i + 1].position);
                }
            }
        }
    }
}
