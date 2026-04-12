using System.Collections.Generic;
using UnityEngine;

namespace DefenseGame.Combat
{
    public class EnemyUnit : MonoBehaviour
    {
        private static readonly List<EnemyUnit> ActiveEnemiesInternal = new List<EnemyUnit>();

        [SerializeField] private float defaultMaxHealth = 5f;
        [SerializeField] private float defaultMoveSpeed = 1.5f;

        private CombatDirector _director;
        private EnemyPath _path;
        private float _currentHealth;
        private float _moveSpeed;
        private int _nextWaypointIndex;
        private bool _isDespawning;

        public static IReadOnlyList<EnemyUnit> ActiveEnemies => ActiveEnemiesInternal;
        public bool IsAlive => !_isDespawning && _currentHealth > 0f;
        public float CurrentHealth => _currentHealth;

        private void OnEnable()
        {
            if (!ActiveEnemiesInternal.Contains(this))
            {
                ActiveEnemiesInternal.Add(this);
            }
        }

        private void OnDisable()
        {
            ActiveEnemiesInternal.Remove(this);
        }

        public void Initialize(EnemyPath path, CombatDirector director, float maxHealth, float moveSpeed)
        {
            _path = path;
            _director = director;
            _currentHealth = maxHealth > 0f ? maxHealth : defaultMaxHealth;
            _moveSpeed = moveSpeed > 0f ? moveSpeed : defaultMoveSpeed;
            _isDespawning = false;

            if (_path != null && _path.WaypointCount > 0)
            {
                Vector3 firstWaypointPosition = _path.GetWaypointPosition(0);
                bool startsAtFirstWaypoint = Vector3.SqrMagnitude(transform.position - firstWaypointPosition) <= 0.0001f;
                _nextWaypointIndex = startsAtFirstWaypoint ? 1 : 0;
            }
            else
            {
                _nextWaypointIndex = 0;
            }
        }

        private void Update()
        {
            if (_isDespawning || _path == null || _path.WaypointCount <= 0)
            {
                return;
            }

            if (_nextWaypointIndex >= _path.WaypointCount)
            {
                ReachGoal();
                return;
            }

            Vector3 target = _path.GetWaypointPosition(_nextWaypointIndex);
            float step = _moveSpeed * Time.deltaTime;
            transform.position = Vector3.MoveTowards(transform.position, target, step);

            if (Vector3.SqrMagnitude(transform.position - target) <= 0.0001f)
            {
                _nextWaypointIndex++;
            }
        }

        public void TakeDamage(float damage)
        {
            if (_isDespawning || damage <= 0f)
            {
                return;
            }

            _currentHealth -= damage;

            if (_currentHealth <= 0f)
            {
                Die();
            }
        }

        private void Die()
        {
            if (_isDespawning)
            {
                return;
            }

            _isDespawning = true;
            _director?.NotifyEnemyDefeated(this);
            Destroy(gameObject);
        }

        private void ReachGoal()
        {
            if (_isDespawning)
            {
                return;
            }

            _isDespawning = true;
            _director?.NotifyEnemyReachedEnd(this);
            Destroy(gameObject);
        }
    }
}
