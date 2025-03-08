using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AIGraph;
using HarmonyLib;
using LevelGeneration;
using Player;

namespace ExtraSurvivalWaveSettings
{
    [HarmonyPatch]
    internal class Patch_SurvivalWave_OnGroupSpawn
    {
        // This must happen here because the spawning should check how far it should spawn for every group

        [HarmonyPatch(typeof(SurvivalWave), nameof(SurvivalWave.SpawnGroup))]
        [HarmonyPrefix]
        public static void SurvivalWave_SpawnGroup(SurvivalWave __instance)
        {
            if (SurvivalWaveManager.Current.LateSpawnTypeOverrideMap.TryGetValue(__instance.EventID, out (SurvivalWaveSpawnType, int) data))
            {
                var type = data.Item1;

                // Because this changes the supplied course node and spawns inside it, we need some reference to the original source zone.
                // `data.Item2` is the destination zone's id.
                AIG_CourseNode node;
                if (!AIG_CourseNode.GetCourseNode(data.Item2, out node))
                {
                    ESWSLogger.Error("Didn't get Course Node from node id??");
                    node = __instance.m_courseNode;
                }
                if (type == SurvivalWaveSpawnType.ClosestToSuppliedNodeButNoBetweenPlayers)
                {
                    __instance.m_courseNode = SurvivalWaveManager.GetNodeAtDistanceFromPlayerToSupplied(node, (int)__instance.m_areaDistance);
                    __instance.m_spawnType = SurvivalWaveSpawnType.InSuppliedCourseNode;
                }
            }
        }

    }
}
