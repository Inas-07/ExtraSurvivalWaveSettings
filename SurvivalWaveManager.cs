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
                            }
                            else
                            {
                                ESWSLogger.Error($"SpawnWave: SpawnType InSuppliedCourseNode(_OnPosition) is specified but cannot find AREA_{(char)('A' + e.Count)} in ({e.DimensionIndex}, {e.Layer}, {e.LocalIndex}), falling back to SpawnType: InSuppliedCourseNodeZone");
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

        internal void OnStopAllWave()
        {
            WaveEventsMap.Clear();
        }

        public void Clear()
        {
            WaveEventsMap.Clear();
        }

        private SurvivalWaveManager() { }

        static SurvivalWaveManager()
        {
            Current = new();
            LevelAPI.OnLevelCleanup += Current.Clear;
        }
    }
}
