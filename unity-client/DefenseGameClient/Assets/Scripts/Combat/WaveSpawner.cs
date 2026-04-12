using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DefenseGame.Combat
{
    public class WaveSpawner : MonoBehaviour
    {
        [Serializable]
        public class SpawnGroup
        {
            [SerializeField] private EnemyUnit enemyPrefab;
            [SerializeField] private int count = 1;
            [SerializeField] private float spawnInterval = 0.75f;
            [SerializeField] private float maxHealth = 5f;
            [SerializeField] private float moveSpeed = 1.5f;

            public EnemyUnit EnemyPrefab => enemyPrefab;
            public int Count => count;
            public float SpawnInterval => spawnInterval;
            public float MaxHealth => maxHealth;
            public float MoveSpeed => moveSpeed;
        }

        [Serializable]
        public class WaveDefinition
        {
            [SerializeField] private string waveName = "Wave";
            [SerializeField] private float startDelay;
            [SerializeField] private List<SpawnGroup> spawnGroups = new List<SpawnGroup>();

            public string WaveName => waveName;
            public float StartDelay => startDelay;
            public IReadOnlyList<SpawnGroup> SpawnGroups => spawnGroups;
        }

        [SerializeField] private Transform explicitSpawnPoint;
        [SerializeField] private Transform enemyParent;
        [SerializeField] private List<WaveDefinition> waves = new List<WaveDefinition>();

        private EnemyPath _path;
        private CombatDirector _director;
        private Coroutine _spawnRoutine;

        public event Action<int, WaveDefinition> WaveStarted;
        public event Action<EnemyUnit> EnemySpawned;
        public event Action SpawnSequenceFinished;

        public int TotalWaveCount => waves.Count;
        public bool IsSpawning => _spawnRoutine != null;

        public void BeginSpawning(EnemyPath path, CombatDirector director)
        {
            _path = path;
            _director = director;

            StopSpawning();
            _spawnRoutine = StartCoroutine(SpawnRoutine());
        }

        public void StopSpawning()
        {
            if (_spawnRoutine == null)
            {
                return;
            }

            StopCoroutine(_spawnRoutine);
            _spawnRoutine = null;
        }

        private IEnumerator SpawnRoutine()
        {
            for (int waveIndex = 0; waveIndex < waves.Count; waveIndex++)
            {
                WaveDefinition wave = waves[waveIndex];
                if (wave == null)
                {
                    continue;
                }

                WaveStarted?.Invoke(waveIndex + 1, wave);

                if (wave.StartDelay > 0f)
                {
                    yield return new WaitForSeconds(wave.StartDelay);
                }

                IReadOnlyList<SpawnGroup> groups = wave.SpawnGroups;
                for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
                {
                    SpawnGroup group = groups[groupIndex];
                    if (group == null || group.EnemyPrefab == null || group.Count <= 0)
                    {
                        continue;
                    }

                    for (int i = 0; i < group.Count; i++)
                    {
                        SpawnEnemy(group);

                        if (group.SpawnInterval > 0f)
                        {
                            yield return new WaitForSeconds(group.SpawnInterval);
                        }
                    }
                }
            }

            _spawnRoutine = null;
            SpawnSequenceFinished?.Invoke();
        }

        private void SpawnEnemy(SpawnGroup group)
        {
            Vector3 spawnPosition = ResolveSpawnPosition();
            Transform parent = enemyParent != null ? enemyParent : null;

            EnemyUnit enemy = Instantiate(group.EnemyPrefab, spawnPosition, Quaternion.identity, parent);
            enemy.Initialize(_path, _director, group.MaxHealth, group.MoveSpeed);
            EnemySpawned?.Invoke(enemy);
        }

        private Vector3 ResolveSpawnPosition()
        {
            if (explicitSpawnPoint != null)
            {
                return explicitSpawnPoint.position;
            }

            if (_path != null && _path.WaypointCount > 0)
            {
                return _path.GetWaypointPosition(0);
            }

            return transform.position;
        }
    }
}
