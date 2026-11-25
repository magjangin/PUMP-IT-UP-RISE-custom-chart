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
        public const string Version = "0.3.12";

        internal static MelonLogger.Instance Log;
        internal static HarmonyLib.Harmony PatchHarmony;
        // 관리 쪽 프레임 번호(OnUpdate마다 +1). 매 노트 경로에서 Time.frameCount 같은 IL2CPP 호출 없이 "이번 프레임에 이미 읽었나"를 판정한다.
        internal static int FrameIndex;
        internal static readonly int[] JudgeCounts = new int[5];

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            PatchHarmony = HarmonyInstance;
            SafePatcher.LogGameBuildInfo();

            ModConfig.Init();
            SaveCustomConfig.InitAndLoad();
            CustomPackManager.EnsureSampleFoldersAndScan();
            // 자켓 PNG를 패치 적용(약 1초) 동안 스레드 풀에서 미리 푼다 — 선곡 리스트가 처음 그려질 때 메인 스레드 디코딩 제거(v0.3.12)
            try { SongTableInjectorPatches.PrefetchCatalogJackets(); }
            catch (System.Exception ex) { Log.Warning($"[CustomJacket] 자켓 미리 디코딩 시작 실패(기존 방식으로 동작): {ex.Message}"); }

            SafePatcher.ApplyAll(HarmonyInstance);

            Log.Msg($"PIURiseCustom v{Version} loaded | Offline={SaveCustomConfig.OfflineMode} | Custom={SaveCustomConfig.CustomEnabled} | Root={ModConfig.ResolvedCustomRoot} | Songs={CustomPackManager.CatalogSongs.Count} | Patches={SafePatcher.AppliedCount} (실패 {SafePatcher.FailedPatches.Count}, 노트 비주얼 훅은 MainStage.Start 때 필요 시 부착)");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            InGameOverlayUI.ClearStage();
            GameplayOptionPatches.ClearStage();
            Log.Msg($"Scene loaded: [{buildIndex}] {sceneName}");
        }

        public override void OnUpdate()
        {
            FrameIndex++;
            // 백그라운드 파일 캐시 예열 결과는 메인 스레드에서 한 번만 출력한다(관리 필드 읽기뿐이라 매 프레임 비용 없음)
            if (CustomPackManager.WarmCacheSummary != null)
                CustomPackManager.FlushWarmCacheLog();
            // 매 프레임 경로: 예외가 새면 MelonLoader가 프레임마다 스택을 찍어 로그가 폭주한다(게임 업데이트로 필드·입력 API가 바뀐 경우 등).
            // 서브시스템별로 격리해 첫 예외만 남기고 나머지 기능은 계속 돈다(v0.3.4).
            try { InGameOverlayUI.UpdateOverlay(); }
            catch (System.Exception ex) { WarnOnce(ref _overlayTickFailed, "OverlayUI", ex); }
            try { CustomMarathonPatches.OnUpdate(); }
            catch (System.Exception ex) { WarnOnce(ref _marathonTickFailed, "Marathon", ex); }
        }

        private static bool _overlayTickFailed, _marathonTickFailed;

        private static void WarnOnce(ref bool flag, string tag, System.Exception ex)
        {
            if (flag) return;
            flag = true;
            Log?.Warning($"[{tag}] 매 프레임 갱신 중 예외(이후 같은 예외는 기록 생략): {ex.GetType().Name}: {ex.Message}");
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
            // 단계마다 격리: 하나가 예외를 던져도 나머지 초기화(특히 노트 훅 부착·마라톤 상태 리셋)는 진행한다
            Step("판정 카운트", Mod.ResetCounts);
            Step("오버레이", InGameOverlayUI.ResetForNewStage);
            Step("노트 비주얼", NoteVisualPatches.ResetForNewStage);
            Step("노트 훅 동기화", () => NoteVisualPatches.LateUpdateHooks.Sync(Mod.PatchHarmony)); // config 핫리로드 결과에 맞춰 매 노트 훅을 붙이거나 뗀다
            Step("플레이 옵션", GameplayOptionPatches.ResetForNewStage);
            Step("마라톤", () => CustomMarathonPatches.OnCustomSongStarted(Util.CurrentSongId()));
            Mod.Log.Msg("MainStage.Start — 인게임 시작");
        }

        private static void Step(string name, System.Action action)
        {
            try { action(); }
            catch (System.Exception ex) { Mod.Log.Warning($"[MainStage.Start] {name} 초기화 오류: {ex.Message}"); }
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
            GameplayOptionPatches.ClearStage();
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
            GameplayOptionPatches.ClearStage();
        }
    }

    [HarmonyPatch(typeof(MainStage), "OnDestroy")]
    internal static class MainStage_OnDestroy_Patch
    {
        private static void Postfix()
        {
            InGameOverlayUI.ClearStage();
            GameplayOptionPatches.ClearStage();
        }
    }
}
