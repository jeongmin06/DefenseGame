using UnityEngine;

namespace DefenseGame.Combat
{
    public class TowerAttack : MonoBehaviour
    {
        [SerializeField] private float attackRange = 3f;
        [SerializeField] private float attackDamage = 1f;
        [SerializeField] private float attackInterval = 0.5f;

        private float _cooldown;

        private void Update()
        {
            _cooldown -= Time.deltaTime;

            if (_cooldown > 0f)
            {
                return;
            }

            EnemyUnit target = FindNearestTargetInRange();
            if (target == null)
            {
                return;
            }

            target.TakeDamage(attackDamage);
            _cooldown = attackInterval > 0f ? attackInterval : 0.1f;
        }

        private EnemyUnit FindNearestTargetInRange()
        {
            float maxRangeSqr = attackRange * attackRange;
            float bestDistanceSqr = maxRangeSqr;
            EnemyUnit bestTarget = null;

            var activeEnemies = EnemyUnit.ActiveEnemies;
            for (int i = 0; i < activeEnemies.Count; i++)
            {
                EnemyUnit enemy = activeEnemies[i];
                if (enemy == null || !enemy.IsAlive)
                {
                    continue;
                }

                float distanceSqr = (enemy.transform.position - transform.position).sqrMagnitude;
                if (distanceSqr <= bestDistanceSqr)
                {
                    bestDistanceSqr = distanceSqr;
                    bestTarget = enemy;
                }
            }

            return bestTarget;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);
        }
    }
}
