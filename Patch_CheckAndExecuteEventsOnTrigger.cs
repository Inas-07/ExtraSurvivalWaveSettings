using HarmonyLib;
using GameData;
using System.Collections;
using Player;
using UnityEngine;
using ExtraSurvivalWaveSettings;
using BepInEx.Unity.IL2CPP.Utils.Collections;

namespace LEGACY
{
    [HarmonyPatch]
    class Patch_CheckAndExecuteEventsOnTrigger
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(WardenObjectiveManager), nameof(WardenObjectiveManager.CheckAndExecuteEventsOnTrigger), new System.Type[] {
            typeof(WardenObjectiveEventData),
            typeof(eWardenObjectiveEventTrigger),
            typeof(bool),
            typeof(float)
        })]
        private static bool Pre_CheckAndExecuteEventsOnTrigger(
            WardenObjectiveEventData eventToTrigger,
            eWardenObjectiveEventTrigger trigger,
            bool ignoreTrigger,
            float currentDuration)
        {
            if (eventToTrigger == null || !ignoreTrigger && eventToTrigger.Trigger != trigger || currentDuration != 0.0 && eventToTrigger.Delay <= currentDuration)
                return true;

            bool _override = false;
            // vanilla event modification
            switch (eventToTrigger.Type)
            {
                case eWardenObjectiveEventType.SpawnEnemyWave:
                case eWardenObjectiveEventType.StopEnemyWaves:
                    _override = true;
                    var coroutine = CoroutineManager.StartCoroutine(Handle(eventToTrigger, 0.0f).WrapToIl2Cpp());
                    WorldEventManager.m_worldEventEventCoroutines.Add(coroutine);
                    break;
            }

            return !_override;
        }

        internal static IEnumerator Handle(WardenObjectiveEventData e, float currentDuration)
        {
            float delay = Mathf.Max(e.Delay - currentDuration, 0f);
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            if (e.Condition.ConditionIndex >= 0
                && WorldEventManager.GetCondition(e.Condition.ConditionIndex) != e.Condition.IsTrue)
            {
                yield break;
            }

            WardenObjectiveManager.DisplayWardenIntel(e.Layer, e.WardenIntel);
            if (e.DialogueID > 0u)
            {
                PlayerDialogManager.WantToStartDialog(e.DialogueID, -1, false, false);
            }

            if (e.SoundID > 0u)
            {
                WardenObjectiveManager.Current.m_sound.Post(e.SoundID, true);
                var line = e.SoundSubtitle.ToString();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    GuiManager.PlayerLayer.ShowMultiLineSubtitle(line);
                }
            }

            switch (e.Type)
            {
                case eWardenObjectiveEventType.SpawnEnemyWave:
                    // take full charge
                    SurvivalWaveManager.Current.SpawnWave(e);
                    break;
                case eWardenObjectiveEventType.StopEnemyWaves:
                    if (string.IsNullOrEmpty(e.WorldEventObjectFilter))
                    {
                        // vanilla 
                        WardenObjectiveManager.StopAlarms();
                        WardenObjectiveManager.StopAllWardenObjectiveEnemyWaves();
                        SurvivalWaveManager.Current.OnStopAllWave();
                    }
                    else
                    {
                        SurvivalWaveManager.Current.StopNamedWaves(e.WorldEventObjectFilter);
                    }
                    break;
            }
        }
    }
}