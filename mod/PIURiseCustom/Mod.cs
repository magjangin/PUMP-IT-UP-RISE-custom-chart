using HarmonyLib;
using Il2Cpp;
using Il2CppPIUMobileStepDLL;
using MelonLoader;

[assembly: MelonInfo(typeof(PIURiseCustom.Mod), "PIURiseCustom", "0.3.0", "화영왕")]
[assembly: MelonGame("ANDAMIRO", "PUMP IT UP RISE")]

namespace PIURiseCustom
{
    public class Mod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        internal static readonly int[] JudgeCounts = new int[5];

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            ModConfig.Init();
            SaveCustomConfig.InitAndLoad();
            CustomPackManager.EnsureSampleFoldersAndScan();

            Log.Msg($"PIURiseCustom v0.3.0 loaded | Offline={SaveCustomConfig.OfflineMode} | Custom={SaveCustomConfig.CustomEnabled} | Root={ModConfig.ResolvedCustomRoot} | Songs={CustomPackManager.CatalogSongs.Count}");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            InGameOverlayUI.ClearStage();
            Log.Msg($"Scene loaded: [{buildIndex}] {sceneName}");
        }

        public override void OnUpdate()
        {
            InGameOverlayUI.UpdateOverlay();
            CustomMarathonPatches.OnUpdate();
        }

        internal static void ResetCounts()
        {
            for (int i = 0; i < JudgeCounts.Length; i++) JudgeCounts[i] = 0;
        }
    }

    // 인게임 시작 시 SaveCustomKey/config.txt 자동 핫리로드 + 판정/비주얼/오버레이 초기화
    [HarmonyPatch(typeof(MainStage), nameof(MainStage.Start))]
    internal static class MainStage_Start_Patch
    {
        private static void Prefix()
        {
            SaveCustomConfig.ReloadIfChanged();
        }

        private static void Postfix()
        {
            Mod.ResetCounts();
            InGameOverlayUI.ResetForNewStage();
            NoteVisualPatches.ResetForNewStage();
            CustomMarathonPatches.OnCustomSongStarted(PlayerData.Instance?.Song?.ID);
            Mod.Log.Msg("MainStage.Start — 인게임 시작");
        }
    }

    [HarmonyPatch(typeof(PUMPPlayer), nameof(PUMPPlayer.JudgeStep_PostProcess))]
    internal static class PUMPPlayer_JudgeStep_PostProcess_Patch
    {
        private static void Postfix(JUDGE __1)
        {
            int j = (int)__1;
            if (j >= 0 && j < Mod.JudgeCounts.Length) Mod.JudgeCounts[j]++;
        }
    }

    [HarmonyPatch(typeof(MainStage), nameof(MainStage.SaveResult))]
    internal static class MainStage_SaveResult_Patch
    {
        private static void Postfix()
        {
            InGameOverlayUI.ClearStage();
            var c = Mod.JudgeCounts;
            Mod.Log.Msg($"MainStage.SaveResult — Perfect {c[0]} / Great {c[1]} / Good {c[2]} / Bad {c[3]} / Miss {c[4]}");
        }
    }

    [HarmonyPatch(typeof(MainStage), "OnDisable")]
    internal static class MainStage_OnDisable_Patch
    {
        private static void Postfix()
        {
            InGameOverlayUI.ClearStage();
        }
    }

    [HarmonyPatch(typeof(MainStage), "OnDestroy")]
    internal static class MainStage_OnDestroy_Patch
    {
        private static void Postfix()
        {
            InGameOverlayUI.ClearStage();
        }
    }
}
