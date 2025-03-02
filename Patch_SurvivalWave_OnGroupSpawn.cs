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
    internal class Patch_SurvivalWaveTest
    {
        // This must happen here because the spawning should check how far it should spawn for every group

        [HarmonyPatch(typeof(SurvivalWave), nameof(SurvivalWave.SpawnGroup))]
        [HarmonyPrefix]
        public static void InfoTestPatch(SurvivalWave __instance)
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
                    __instance.m_courseNode = GetNodeAtDistanceFromPlayerToSupplied(node, (int)__instance.m_areaDistance);
                    __instance.m_spawnType = SurvivalWaveSpawnType.InSuppliedCourseNode;
                }
            }
        }

        public static AIG_CourseNode GetNodeAtDistanceFromPlayerToSupplied(AIG_CourseNode dest_node, int area_distance)
        {
            // If below fails, default to the defined node as if it were `InSuppliedCourseNode`
            // Could maybe instead try to find the closest node to dest before blocked if it's blocked?
            var NodeToSpawnAt = dest_node;

            int maxdist = int.MaxValue;
            AIG_CourseNode[] closestPath = null;
            foreach (var player in PlayerManager.PlayerAgentsInLevel)
            {
                if (IsNodeReachable(player.m_courseNode, dest_node, out AIG_CourseNode[] path) && path.Length < maxdist)
                {
                    closestPath = path;
                    maxdist = path.Length;
                }
            }

            if (closestPath != null)
            {
                if (closestPath.Length > area_distance)
                    NodeToSpawnAt = closestPath[area_distance];
            }
            else
                ESWSLogger.Error("GetNodeAtDistanceFromPlayerToSupplied: No path from any player to supplied node!");

            return NodeToSpawnAt;

        }

        // Checks if the target node can be reached by the source node, and returns the node path up to (but not including) the target node
        // "Reachable" refers to there being no closed security doors between source and target.
        // This was originally made by randomuserhi, based on `AIG_CourseGraph.GetDistanceBetweenToNodes()` to account for closed doors
        private static bool IsNodeReachable(AIG_CourseNode source, AIG_CourseNode target, out AIG_CourseNode[] pathToNode)
        {
            pathToNode = null;

            if (source == null || target == null) return false;
            if (source.NodeID == target.NodeID)
            {
                pathToNode = [];
                return true;
            }

            AIG_SearchID.IncrementSearchID();
            ushort searchID = AIG_SearchID.SearchID;
            Queue<(AIG_CourseNode, AIG_CourseNode[])> queue = new Queue<(AIG_CourseNode, AIG_CourseNode[])>();
            queue.Enqueue((source, []));

            while (queue.Count > 0)
            {
                var (current, path) = queue.Dequeue();
                current.m_searchID = searchID;

                AIG_CourseNode[] newPath = [.. path, current];

                foreach (AIG_CoursePortal portal in current.m_portals)
                {
                    LG_SecurityDoor? secDoor = portal.Gate?.SpawnedDoor?.TryCast<LG_SecurityDoor>();
                    if (secDoor != null)
                    {
                        if (secDoor.LastStatus != eDoorStatus.Open && secDoor.LastStatus != eDoorStatus.Opening)
                            continue;
                    }
                    AIG_CourseNode nextNode = portal.GetOppositeNode(current);
                    if (nextNode.m_searchID == searchID) continue;
                    if (nextNode.NodeID == target.NodeID)
                    {
                        pathToNode = newPath;
                        return true;
                    }

                    queue.Enqueue((nextNode, newPath));
                }
            }

            return false;
        }

    }
}
