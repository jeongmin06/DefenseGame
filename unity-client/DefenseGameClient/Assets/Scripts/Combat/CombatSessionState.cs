namespace DefenseGame.Combat
{
    public class CombatSessionState
    {
        public int MaxEscapesBeforeDefeat { get; }
        public int SpawnedCount { get; private set; }
        public int AliveCount { get; private set; }
        public int DefeatedCount { get; private set; }
        public int EscapedCount { get; private set; }
        public int StartedWaveCount { get; private set; }
        public bool AllWavesSpawned { get; private set; }
        public BattleResult Result { get; private set; }

        public CombatSessionState(int maxEscapesBeforeDefeat)
        {
            MaxEscapesBeforeDefeat = maxEscapesBeforeDefeat;
            Result = BattleResult.None;
        }

        public void RegisterWaveStarted()
        {
            if (Result != BattleResult.None)
            {
                return;
            }

            StartedWaveCount++;
        }

        public void RegisterSpawn()
        {
            if (Result != BattleResult.None)
            {
                return;
            }

            SpawnedCount++;
            AliveCount++;
        }

        public void RegisterDefeat()
        {
            if (AliveCount > 0)
            {
                AliveCount--;
            }

            DefeatedCount++;
        }

        public void RegisterEscape()
        {
            if (AliveCount > 0)
            {
                AliveCount--;
            }

            EscapedCount++;
        }

        public void MarkAllWavesSpawned()
        {
            AllWavesSpawned = true;
        }

        public void SetResult(BattleResult result)
        {
            if (Result != BattleResult.None)
            {
                return;
            }

            Result = result;
        }
    }
}
