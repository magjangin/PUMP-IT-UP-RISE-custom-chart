using HarmonyLib;
using Il2Cpp;
using Il2CppPIUMobileStepDLL;
using MelonLoader;

[assembly: MelonInfo(typeof(PIURiseSkeleton.Mod), "PIURiseSkeleton", "0.1.0", "magjangin")]
[assembly: MelonGame("ANDAMIRO", "PUMP IT UP RISE")]

namespace PIURiseSkeleton
{
    // 읽기 전용 스켈레톤: 판정/점수 값을 바꾸지 않고 로그만 남긴다.
    // 이 게임은 온라인 기록·멀티 기능이 있어 값을 바꾸는 패치는 기록 오염 위험이 있다(docs\hooks.md 참고).
    public class Mod : MelonMod
    {
        internal static MelonLogger.Instance Log;
        // JUDGE 0 Perfect, 1 Great, 2 Good, 3 Bad, 4 Miss
        internal static readonly int[] JudgeCounts = new int[5];

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;
            Log.Msg("PIURiseSkeleton loaded (read-only hooks: MainStage.Start, PUMPPlayer.JudgeStep_PostProcess, MainStage.SaveResult)");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            Log.Msg($"Scene loaded: [{buildIndex}] {sceneName}");
        }

        internal static void ResetCounts()
        {
            for (int i = 0; i < JudgeCounts.Length; i++) JudgeCounts[i] = 0;
        }
    }

    [HarmonyPatch(typeof(MainStage), nameof(MainStage.Start))]
    internal static class MainStage_Start_Patch
    {
        private static void Postfix()
        {
            Mod.ResetCounts();
            Mod.Log.Msg("MainStage.Start — 인게임 스테이지 시작, 판정 카운터 초기화");
        }
    }

    // RVA 0x79A7B0 — 판정 1회마다 호출 (logs\JudgeStep_PostProcess.rea.c)
    [HarmonyPatch(typeof(PUMPPlayer), nameof(PUMPPlayer.JudgeStep_PostProcess))]
    internal static class PUMPPlayer_JudgeStep_PostProcess_Patch
    {
        private static void Postfix(JUDGE __1)
        {
            int j = (int)__1;
            if (j >= 0 && j < Mod.JudgeCounts.Length) Mod.JudgeCounts[j]++;
        }
    }

    // RVA 0x680FB0 — 곡 종료 후 결과 생성 진입 (logs\SaveResult.rea.c)
    [HarmonyPatch(typeof(MainStage), nameof(MainStage.SaveResult))]
    internal static class MainStage_SaveResult_Patch
    {
        private static void Postfix()
        {
            var c = Mod.JudgeCounts;
            Mod.Log.Msg($"MainStage.SaveResult — Perfect {c[0]} / Great {c[1]} / Good {c[2]} / Bad {c[3]} / Miss {c[4]}");
        }
    }
}
