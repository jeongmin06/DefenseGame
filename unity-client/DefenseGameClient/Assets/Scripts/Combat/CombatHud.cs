using UnityEngine;

namespace DefenseGame.Combat
{
    public class CombatHud : MonoBehaviour
    {
        [SerializeField] private CombatDirector combatDirector;
        [SerializeField] private Vector2 panelPosition = new Vector2(16f, 16f);
        [SerializeField] private Vector2 panelSize = new Vector2(260f, 150f);

        private void OnGUI()
        {
            if (combatDirector == null)
            {
                return;
            }

            Rect panelRect = new Rect(panelPosition.x, panelPosition.y, panelSize.x, panelSize.y);
            GUILayout.BeginArea(panelRect, GUI.skin.box);
            GUILayout.Label($"상태: {combatDirector.StatusText}");
            GUILayout.Label($"웨이브: {combatDirector.CurrentWaveIndex}/{combatDirector.TotalWaveCount}");
            GUILayout.Label($"생성: {combatDirector.SpawnedCount}");
            GUILayout.Label($"생존: {combatDirector.AliveCount}");
            GUILayout.Label($"처치: {combatDirector.DefeatedCount}");
            GUILayout.Label($"도착: {combatDirector.EscapedCount}/{combatDirector.MaxEscapesBeforeDefeat}");

            if (combatDirector.Result == BattleResult.Victory)
            {
                GUILayout.Space(8f);
                GUILayout.Label("모든 웨이브를 막아냈습니다.");
            }
            else if (combatDirector.Result == BattleResult.Defeat)
            {
                GUILayout.Space(8f);
                GUILayout.Label("적이 방어선을 돌파했습니다.");
            }

            GUILayout.EndArea();
        }
    }
}
