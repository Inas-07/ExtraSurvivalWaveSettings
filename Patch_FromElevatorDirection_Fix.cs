using AIGraph;
using HarmonyLib;
using Player;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SurvivalWave;
using UnityEngine;

namespace ExtraSurvivalWaveSettings
{
    [HarmonyPatch]
    internal static class Patch_FromElevatorDirection_Fix
    {
        // FromElevatorDirection fix
        [HarmonyPatch(typeof(SurvivalWave), nameof(SurvivalWave.GetScoredSpawnPoint_FromElevator))]
        [HarmonyPrefix]
        private static bool GetScoredSpawnPoint_FromElevator(SurvivalWave __instance, ref ScoredSpawnPoint __result)
        {
            AIG_CourseNode startCourseNode = __instance.m_courseNode.m_dimension.GetStartCourseNode();
            AIG_CourseNode? courseNode = null;

            int closestDist = int.MaxValue;
            // Find closest reachable alive player
            foreach (PlayerAgent player in PlayerManager.PlayerAgentsInLevel)
            {
                if (player.Alive && SurvivalWaveManager.IsNodeReachable(startCourseNode, player.m_courseNode, out var path) && path.Length < closestDist)
                {
                    courseNode = player.m_courseNode;
                    closestDist = path.Length;
                }
            }
            if (courseNode == null) return true;

            Vector3 normalized = (startCourseNode.Position - courseNode.Position).normalized;
            normalized.y = 0f;
            Il2CppSystem.Collections.Generic.List<ScoredSpawnPoint> availableSpawnPoints = __instance.GetAvailableSpawnPointsBetweenElevatorAndNode(courseNode);
            ScoredSpawnPoint bestSP = new() { totalCost = float.MinValue };
            Vector3 position = courseNode.Position;
            float baseCost = 1f;
            float costFactor = 4f - baseCost;

            foreach (ScoredSpawnPoint currentSP in availableSpawnPoints)
            {
                Vector3 direction = currentSP.firstCoursePortal.Position - position;
                direction.y = 0f;
                direction.Normalize();
                currentSP.m_dir = direction;
                currentSP.totalCost = Mathf.Clamp01(Vector3.Dot(direction, normalized));

                if (currentSP.pathHeat > baseCost - 0.01f)
                {
                    currentSP.totalCost += 1f + (1f - Mathf.Clamp(currentSP.pathHeat - baseCost, 0f, costFactor) / costFactor);
                }
                if (bestSP == null)
                {
                    bestSP = currentSP;
                }
                else if (currentSP.totalCost > bestSP.totalCost)
                {
                    bestSP = currentSP;
                }
            }

            bestSP.courseNode ??= courseNode;
            __result = bestSP;

            return false;
        }
    }
}
