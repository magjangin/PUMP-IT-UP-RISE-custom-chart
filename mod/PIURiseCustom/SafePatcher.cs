using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using HarmonyLib;
using MelonLoader.Utils;

namespace PIURiseCustom
{
    /// <summary>
    /// 게임 업데이트 내성용 패치 적용기.
    ///
    /// MelonLoader 기본 동작(HarmonyInstance.PatchAll)은 패치 클래스 하나가 실패하면(업데이트로 메서드가 사라지거나
    /// 이름·시그니처가 바뀐 경우) 예외로 중단되어 그 뒤의 패치가 전부 빠지거나 모드 초기화가 통째로 실패한다.
    /// 그래서 [assembly: HarmonyDontPatchAll]로 일괄 패치를 끄고, 여기서 패치 클래스마다 따로 적용·격리한다.
    /// 실패한 패치는 로그에 이름을 남기고 나머지 기능은 그대로 동작한다.
    /// </summary>
    internal static class SafePatcher
    {
        // 실기 검증을 마친 빌드 (AGENTS.md §1). 다르면 경고만 하고 계속 진행한다.
        internal const string VerifiedSteamBuildId = "26399668";
        internal const string VerifiedGameVersion = "1.6.0";

        internal static readonly List<string> FailedPatches = new();
        internal static int AppliedCount { get; private set; }

        public static void ApplyAll(HarmonyLib.Harmony harmony)
        {
            FailedPatches.Clear();
            AppliedCount = 0;

            var patchTypes = AccessTools.GetTypesFromAssembly(typeof(SafePatcher).Assembly)
                .Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToList();

            var failedByGroup = new SortedDictionary<string, int>(StringComparer.Ordinal);
            int offlineFailed = 0;

            foreach (var type in patchTypes)
            {
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    AppliedCount++;
                }
                catch (Exception ex)
                {
                    FailedPatches.Add(type.Name);
                    // 패치 클래스는 기능별 정적 클래스(OfflineStubPatches 등) 안에 중첩돼 있다 → 바깥 클래스 이름이 곧 기능 그룹
                    string group = type.DeclaringType?.Name ?? "Mod";
                    failedByGroup[group] = failedByGroup.TryGetValue(group, out int n) ? n + 1 : 1;
                    if (type.DeclaringType == typeof(OfflineStubPatches)) offlineFailed++;

                    var root = ex;
                    while (root.InnerException != null) root = root.InnerException;
                    Mod.Log.Warning($"[Patch] 적용 실패(해당 기능만 비활성): {type.FullName} — {root.GetType().Name}: {root.Message}");
                }
            }

            if (FailedPatches.Count == 0)
            {
                Mod.Log.Msg($"[Patch] Harmony 패치 {AppliedCount}/{patchTypes.Count}개 적용 완료");
            }
            else
            {
                string groups = string.Join(", ", failedByGroup.Select(kv => $"{kv.Key} {kv.Value}개"));
                Mod.Log.Warning($"[Patch] Harmony 패치 {AppliedCount}/{patchTypes.Count}개 적용, 실패 {FailedPatches.Count}개 [{groups}]: {string.Join(", ", FailedPatches)} " +
                                "— 게임 업데이트로 대상 메서드가 바뀌었을 수 있습니다. docs/hooks.md 업데이트 점검 절차를 따르세요.");
            }

            // 오프라인 관문이 하나라도 빠지면 그 경로는 실제 서버로 나간다(로그인·기록 전송 등). 다른 기능 실패와 달리 조용히 넘기면 안 된다.
            if (offlineFailed > 0 && SaveCustomConfig.OfflineMode)
                Mod.Log.Error($"[Patch] 오프라인 스텁 패치 {offlineFailed}개 적용 실패 — OfflineMode=1이어도 일부 통신이 실제 서버로 나갈 수 있습니다. " +
                              "고치기 전에는 온라인 계정으로 플레이하지 마세요 (docs/hooks.md §5-6).");
        }

        /// <summary>
        /// 실행 중인 게임 빌드가 검증 빌드와 같은지 로그로 알린다 (Steam appmanifest의 buildid + Application.version).
        /// 정보 제공용이며 실패해도 아무 영향이 없다.
        /// </summary>
        public static void LogGameBuildInfo()
        {
            string buildId = TryReadSteamBuildId();
            string appVersion = null;
            try { appVersion = UnityEngine.Application.version; } catch { }

            bool buildMismatch = buildId != null && buildId != VerifiedSteamBuildId;
            bool versionMismatch = appVersion != null && appVersion != VerifiedGameVersion;

            string info = $"[Build] Steam buildid={buildId ?? "(알 수 없음)"}, 게임 버전={appVersion ?? "(알 수 없음)"} " +
                          $"(검증 빌드 {VerifiedSteamBuildId} / {VerifiedGameVersion})";

            if (buildMismatch || versionMismatch)
                Mod.Log.Warning(info + " — 검증되지 않은 빌드입니다. 패치 실패·오동작 시 AGENTS.md §1, docs/hooks.md를 참고하세요.");
            else
                Mod.Log.Msg(info);
        }

        private static string TryReadSteamBuildId()
        {
            try
            {
                // <steamapps>\common\<게임> → <steamapps>\appmanifest_2756930.acf
                var gameDir = new DirectoryInfo(MelonEnvironment.GameRootDirectory);
                var steamApps = gameDir.Parent?.Parent;
                if (steamApps == null) return null;

                string acf = Path.Combine(steamApps.FullName, "appmanifest_2756930.acf");
                if (!File.Exists(acf)) return null;

                var m = Regex.Match(File.ReadAllText(acf), "\"buildid\"\\s*\"(\\d+)\"", RegexOptions.IgnoreCase);
                return m.Success ? m.Groups[1].Value : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
