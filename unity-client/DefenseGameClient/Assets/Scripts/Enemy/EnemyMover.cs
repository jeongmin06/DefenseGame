using UnityEngine;

namespace DefenseGame.Enemy
{
    public class EnemyMover : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float moveSpeed = 1.5f;
        [SerializeField] private float arriveDistance = 0.05f;

        public bool HasReachedTarget { get; private set; }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            HasReachedTarget = false;
        }

        private void Update()
        {
            if (target == null || moveSpeed <= 0f || HasReachedTarget)
            {
                return;
            }

            Vector3 targetPosition = target.position;
            transform.position = Vector3.MoveTowards(
                transform.position,
                targetPosition,
                moveSpeed * Time.deltaTime);

            float sqrDistanceToTarget = (transform.position - targetPosition).sqrMagnitude;
            HasReachedTarget = sqrDistanceToTarget <= arriveDistance * arriveDistance;
        }
    }
}
