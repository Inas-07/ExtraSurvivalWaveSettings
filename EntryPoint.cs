using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace ExtraSurvivalWaveSettings
{
    [BepInDependency("com.dak.MTFO")]
    [BepInDependency("dev.gtfomodding.gtfo-api", BepInDependency.DependencyFlags.HardDependency)]
    [BepInIncompatibility("Inas07.LEGACY")]
    [BepInPlugin(AUTHOR + "." + PLUGIN_NAME, PLUGIN_NAME, VERSION)]
    
    public class EntryPoint: BasePlugin
    {
        public const string AUTHOR = "Inas07";
        public const string PLUGIN_NAME = "ExtraSurvivalWaveSettings";
        public const string VERSION = "1.0.0";

        private Harmony m_Harmony;
        
        public override void Load()
        {
            m_Harmony = new Harmony("ExtraSurvivalWaveSettings");
            m_Harmony.PatchAll();
        }
    }
}

