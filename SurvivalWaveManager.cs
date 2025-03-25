using AIGraph;
using GameData;
using LevelGeneration;
using Player;
using SNetwork;
using GTFO.API;
using System.Collections.Generic;
using System.Collections.Concurrent;
using AK;
using UnityEngine;

namespace ExtraSurvivalWaveSettings
{
    public class SurvivalWaveManager
    {
        public static readonly SurvivalWaveManager Current;

        // ISSUE: wave name are not registered on client side 
        private ConcurrentDictionary<string, List<ushort>> WaveEventsMap = new();
        public Dictionary<uint, (SurvivalWaveSpawnType, int)> LateSpawnTypeOverrideMap = new();

        public void SpawnWave(WardenObjectiveEventData e)
        {
            if (!SNet.IsMaster) return;
            PlayerAgent localPlayer = PlayerManager.GetLocalPlayerAgent();
            if (localPlayer == null)
            {
                ESWSLogger.Error("SpawnWave: LocalPlayerAgent is null, wtf?");
                return;
            }

            GenericEnemyWaveData waveData = e.EnemyWaveData;
            if (waveData.WaveSettings == 0 || waveData.WavePopulation == 0)
            {
                ESWSLogger.Error("SpawnWave: WaveSettings or WavePopulation is 0");
                return;
            }

            var waveSettingDB = GameDataBlockBase<SurvivalWaveSettingsDataBlock>.GetBlock(waveData.WaveSettings);
            var wavePopulation = GameDataBlockBase<SurvivalWavePopulationDataBlock>.GetBlock(waveData.WavePopulation);
            if (waveSettingDB == null || wavePopulation == null) return;

            AIG_CourseNode spawnNode = localPlayer.CourseNode;
            SurvivalWaveSpawnType spawnType = SurvivalWaveSpawnType.InRelationToClosestAlivePlayer;
            Vector3 spawnPosition = Vector3.zero;

            // spawn type override 
            if (waveSettingDB.m_overrideWaveSpawnType == true)
            {
                if (waveSettingDB.m_survivalWaveSpawnType != SurvivalWaveSpawnType.InSuppliedCourseNodeZone
                    && waveSettingDB.m_survivalWaveSpawnType != SurvivalWaveSpawnType.InSuppliedCourseNode
                    && waveSettingDB.m_survivalWaveSpawnType != SurvivalWaveSpawnType.InSuppliedCourseNode_OnPosition
                    && waveSettingDB.m_survivalWaveSpawnType != SurvivalWaveSpawnType.ClosestToSuppliedNodeButNoBetweenPlayers
                    )
                {
                    spawnType = waveSettingDB.m_survivalWaveSpawnType;
                }

                else
                {
                    if (Builder.CurrentFloor.TryGetZoneByLocalIndex(e.DimensionIndex, e.Layer, e.LocalIndex, out var zone) && zone != null)
                    {
                        spawnNode = zone.m_courseNodes[0];
                        spawnType = SurvivalWaveSpawnType.InSuppliedCourseNodeZone;

                        if (waveSettingDB.m_survivalWaveSpawnType == SurvivalWaveSpawnType.InSuppliedCourseNode || waveSettingDB.m_survivalWaveSpawnType == SurvivalWaveSpawnType.InSuppliedCourseNode_OnPosition)
                        {
                            if (e.Count < zone.m_courseNodes.Count)
                            {
                                spawnNode = zone.m_courseNodes[e.Count];
                                spawnType = SurvivalWaveSpawnType.InSuppliedCourseNode;
                                if(waveSettingDB.m_survivalWaveSpawnType == SurvivalWaveSpawnType.InSuppliedCourseNode_OnPosition)
                                {
                                    spawnPosition = e.Position;
                                    spawnType = SurvivalWaveSpawnType.InSuppliedCourseNode_OnPosition; 
                                }
                                else if (waveSettingDB.m_survivalWaveSpawnType == SurvivalWaveSpawnType.ClosestToSuppliedNodeButNoBetweenPlayers)
                                {
                                    spawnType = SurvivalWaveSpawnType.ClosestToSuppliedNodeButNoBetweenPlayers;
                                }
                            }
                            else
                            {
                                ESWSLogger.Error($"SpawnWave: SpawnType {waveSettingDB.m_survivalWaveSpawnType} is specified but cannot find AREA_{(char)('A' + e.Count)} in ({e.DimensionIndex}, {e.Layer}, {e.LocalIndex}), falling back to SpawnType: InSuppliedCourseNodeZone");
                            }
                        }
                    }
                    else
                    {
                        ESWSLogger.Error($"SpawnWave: Failed to find zone with GlobalIndex ({e.DimensionIndex}, {e.Layer}, {e.LocalIndex}), falling back to default spawn type");
                    }
                }
            }

            // start wave
            ushort eventID;
            if (!Mastermind.Current.TriggerSurvivalWave(
                spawnNode, waveData.WaveSettings, waveData.WavePopulation, 
                out eventID, 
                spawnType: spawnType, 
                spawnDelay: waveData.SpawnDelay, 
                position: spawnPosition,
                areaDistance: waveData.AreaDistance)) 
            {
                ESWSLogger.Error("SpawnWave: Critical ERROR! Failed spawning enemy wave");
                return;
            }

            WardenObjectiveManager.RegisterSurvivalWaveID(eventID);
            ESWSLogger.Log($"SpawnWave: Enemy wave spawned ({spawnType}) with eventID {eventID}");

            if (waveData.TriggerAlarm)
            {
                WardenObjectiveManager.Current.m_sound.UpdatePosition(spawnNode.Position);
                WardenObjectiveManager.Current.m_sound.Post(EVENTS.APEX_PUZZLE_START_ALARM);
                ESWSLogger.Debug("SpawnWave: Trigger Alarm (prolly bugged when there're multiple alarms)");
            }

            if (!string.IsNullOrEmpty(waveData.IntelMessage))
            {
                GuiManager.PlayerLayer.m_wardenIntel.ShowSubObjectiveMessage("", waveData.IntelMessage);
            }

            if (spawnType == SurvivalWaveSpawnType.InSuppliedCourseNodeZone)
            {
                ESWSLogger.Log($"Spawning in: ({e.DimensionIndex}, {e.Layer}, {e.LocalIndex})");
            }
            else if (spawnType == SurvivalWaveSpawnType.InSuppliedCourseNode)
            {
                ESWSLogger.Log($"Spawning in: ({e.DimensionIndex}, {e.Layer}, {e.LocalIndex}, AREA_{(char)('A' + e.Count)})");
            }
            else if (spawnType == SurvivalWaveSpawnType.InSuppliedCourseNode_OnPosition)
            {
                ESWSLogger.Log($"Spawning in: ({e.DimensionIndex}, {e.Layer}, {e.LocalIndex}, AREA_{(char)('A' + e.Count)}), position: {e.Position}");
            }
            else if (spawnType == SurvivalWaveSpawnType.ClosestToSuppliedNodeButNoBetweenPlayers)
            {
                ESWSLogger.Log($"Spawning closest to: ({e.DimensionIndex}, {e.Layer}, {e.LocalIndex}, AREA_{(char)('A' + e.Count)})");
                LateSpawnTypeOverrideMap.Add(eventID, (SurvivalWaveSpawnType.ClosestToSuppliedNodeButNoBetweenPlayers, spawnNode.NodeID));
            }

            string waveName = e.WorldEventObjectFilter;
            // check if wave is named
            if (!string.IsNullOrEmpty(waveName))
            {
                if (!WaveEventsMap.ContainsKey(waveName))
                {
                    WaveEventsMap[waveName] = new();
                }

                WaveEventsMap[waveName].Add(eventID);
                ESWSLogger.Debug($"SpawnWave: Registered wave with filter {waveName}, number of waves assigned: {WaveEventsMap[waveName].Count}");
            }
        }

        public void StopNamedWaves(string waveName)
        {
            if (string.IsNullOrEmpty(waveName)) return;

            if(!WaveEventsMap.Remove(waveName, out var eventIDList))
            {
                ESWSLogger.Error($"Wave Filter {waveName} is unregistered, cannot stop wave.");
                return;
            }

            ESWSLogger.Debug($"StopNamedWaves: Stopping waves with name {waveName}, wave count: {eventIDList.Count}");
            eventIDList.ForEach(eventID =>
            {
                LateSpawnTypeOverrideMap.Remove(eventID);
                if (Mastermind.Current.TryGetEvent(eventID, out var masterMindEvent_StopWave))
                {
                    masterMindEvent_StopWave.StopEvent();
                }
                else
                {
                    ESWSLogger.Error($"Wave with ID {eventID}: cannot find event. Are you debugging a level?");
                }
            });
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
        internal static bool IsNodeReachable(AIG_CourseNode source, AIG_CourseNode target, out AIG_CourseNode[] pathToNode)
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

        internal void OnStopAllWave()
        {
            // Clarity that it's referring to the below method.
            this.Clear();
        }

        public void Clear()
        {
            WaveEventsMap.Clear();
            LateSpawnTypeOverrideMap.Clear();
        }

        private SurvivalWaveManager() { }

        static SurvivalWaveManager()
        {
            Current = new();
            LevelAPI.OnLevelCleanup += Current.Clear;
        }
    }
}
