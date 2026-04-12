using UnityEngine;

namespace DefenseGame.Combat
{
    public class CombatDirector : MonoBehaviour
    {
        [SerializeField] private EnemyPath enemyPath;
        [SerializeField] private WaveSpawner waveSpawner;
        [SerializeField] private int maxEscapesBeforeDefeat = 10;

        private CombatSessionState _state;
        private int _currentWaveIndex;

        public BattleResult Result => _state == null ? BattleResult.None : _state.Result;
        public int SpawnedCount => _state == null ? 0 : _state.SpawnedCount;
        public int AliveCount => _state == null ? 0 : _state.AliveCount;
        public int DefeatedCount => _state == null ? 0 : _state.DefeatedCount;
        public int EscapedCount => _state == null ? 0 : _state.EscapedCount;
        public int MaxEscapesBeforeDefeat => _state == null ? maxEscapesBeforeDefeat : _state.MaxEscapesBeforeDefeat;
        public int CurrentWaveIndex => _currentWaveIndex;
        public int TotalWaveCount => waveSpawner == null ? 0 : waveSpawner.TotalWaveCount;

        public string StatusText
        {
            get
            {
                if (_state == null)
                {
                    return "전투 준비 중";
                }

                if (Result == BattleResult.Victory)
                {
                    return "승리";
                }

                if (Result == BattleResult.Defeat)
                {
                    return "패배";
                }

                if (_currentWaveIndex > 0 && TotalWaveCount > 0)
                {
                    return "웨이브 진행 중";
                }

                return "웨이브 대기 중";
            }
        }

        private void OnEnable()
        {
            if (waveSpawner != null)
            {
                waveSpawner.WaveStarted += HandleWaveStarted;
                waveSpawner.EnemySpawned += HandleEnemySpawned;
                waveSpawner.SpawnSequenceFinished += HandleSpawnSequenceFinished;
            }
        }

        private void OnDisable()
        {
            if (waveSpawner != null)
            {
                waveSpawner.WaveStarted -= HandleWaveStarted;
                waveSpawner.EnemySpawned -= HandleEnemySpawned;
                waveSpawner.SpawnSequenceFinished -= HandleSpawnSequenceFinished;
            }
        }

        private void Start()
        {
            _state = new CombatSessionState(maxEscapesBeforeDefeat);
            _currentWaveIndex = 0;

            if (enemyPath == null || enemyPath.WaypointCount < 2)
            {
                Debug.LogError("CombatDirector는 waypoint가 2개 이상인 EnemyPath가 필요합니다.", this);
                _state.SetResult(BattleResult.Defeat);
                return;
            }

            if (waveSpawner != null)
            {
                waveSpawner.BeginSpawning(enemyPath, this);
            }
            else
            {
                _state.MarkAllWavesSpawned();
                TryResolveBattleResult();
            }
        }

        public void NotifyEnemyDefeated(EnemyUnit enemy)
        {
            if (_state == null || _state.Result != BattleResult.None)
            {
                return;
            }

            _state.RegisterDefeat();
            TryResolveBattleResult();
        }

        public void NotifyEnemyReachedEnd(EnemyUnit enemy)
        {
            if (_state == null || _state.Result != BattleResult.None)
            {
                return;
            }

            _state.RegisterEscape();
            TryResolveBattleResult();
        }

        private void HandleWaveStarted(int waveIndex, WaveSpawner.WaveDefinition wave)
        {
            if (_state == null || _state.Result != BattleResult.None)
            {
                return;
            }

            _currentWaveIndex = waveIndex;
            _state.RegisterWaveStarted();
        }

        private void HandleEnemySpawned(EnemyUnit enemy)
        {
            if (_state == null || _state.Result != BattleResult.None)
            {
                return;
            }

            _state.RegisterSpawn();
        }

        private void HandleSpawnSequenceFinished()
        {
            if (_state == null || _state.Result != BattleResult.None)
            {
                return;
            }

            _state.MarkAllWavesSpawned();
            TryResolveBattleResult();
        }

        private void TryResolveBattleResult()
        {
            if (_state == null || _state.Result != BattleResult.None)
            {
                return;
            }

            if (_state.EscapedCount >= _state.MaxEscapesBeforeDefeat)
            {
                _state.SetResult(BattleResult.Defeat);
                waveSpawner?.StopSpawning();
                return;
            }

            if (_state.AllWavesSpawned && _state.AliveCount == 0)
            {
                _state.SetResult(BattleResult.Victory);
            }
        }
    }
}
