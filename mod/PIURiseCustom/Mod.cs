using HarmonyLib;
using Il2Cpp;
using Il2CppPIUMobileStepDLL;
using MelonLoader;

[assembly: MelonInfo(typeof(PIURiseCustom.Mod), "PIURiseCustom", PIURiseCustom.Mod.Version, "화영왕")]
[assembly: MelonGame("ANDAMIRO", "PUMP IT UP RISE")]
// 일괄 PatchAll 대신 SafePatcher가 패치 클래스별로 격리 적용한다 (업데이트 내성, SafePatcher.cs 참고)
[assembly: HarmonyDontPatchAll]

namespace PIURiseCustom
{
    public class Mod : MelonMod
    {
        public const string Version = "0.3.1";

        internal static MelonLogger.Instance Log;
        internal static readonly int[] JudgeCounts = new int[5];

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            SafePatcher.LogGameBuildInfo();

            ModConfig.Init();
            SaveCustomConfig.InitAndLoad();
            CustomPackManager.EnsureSampleFoldersAndScan();

            SafePatcher.ApplyAll(HarmonyInstance);

            Log.Msg($"PIURiseCustom v{Version} loaded | Offline={SaveCustomConfig.OfflineMode} | Custom={SaveCustomConfig.CustomEnabled} | Root={ModConfig.ResolvedCustomRoot} | Songs={CustomPackManager.CatalogSongs.Count} | Patches={SafePatcher.AppliedCount} (실패 {SafePatcher.FailedPatches.Count})");
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
            try { SaveCustomConfig.ReloadIfChanged(); }
            catch (System.Exception ex) { Mod.Log.Warning($"[Config] 핫리로드 오류: {ex.Message}"); }
        }

        private static void Postfix()
        {
            Mod.ResetCounts();
            InGameOverlayUI.ResetForNewStage();
            NoteVisualPatches.ResetForNewStage();
            CustomMarathonPatches.OnCustomSongStarted(Util.CurrentSongId());
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
