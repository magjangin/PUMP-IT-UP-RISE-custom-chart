using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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

            var sw = System.Diagnostics.Stopwatch.StartNew();
            int preparedMethods = 0;

            foreach (var type in patchTypes)
            {
                try
                {
                    // 패치 본문을 걸기 전에 미리 JIT한다(v0.3.6). 게임 업데이트로 interop에서 필드·메서드가 사라지면
                    // 예전엔 그 패치가 처음 불릴 때 MissingFieldException 등이 나고, Il2CppInterop 트램펄린이 예외를 삼켜
                    // Prefix라면 원본 메서드까지 조용히 건너뛰었다. 여기서 실패하면 패치를 아예 걸지 않아 원본이 그대로 돈다.
                    preparedMethods += PrecompileType(type);
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

                    Mod.Log.Warning($"[Patch] 적용 실패(해당 기능만 비활성): {type.FullName} — {DescribeRoot(ex)}");
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

            // 패치가 부르는 도우미(파서·오버레이·스텁 응답 등)도 같은 방식으로 미리 JIT한다.
            // 실패해도 이미 건 패치는 그대로 두되(도우미 호출부는 대부분 try로 감싸 있음) 어떤 기능이 깨질지 부팅 로그에 남긴다.
            // 덤으로 첫 판정·첫 곡 진입 때 몰리던 JIT 지연도 부팅 시간으로 옮겨진다.
            var helperFailures = new List<string>();
            bool offlineHelperFailed = false;
            var patchTypeSet = new HashSet<Type>(patchTypes);
            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(SafePatcher).Assembly))
            {
                if (type.IsNested || patchTypeSet.Contains(type))
                    continue; // 중첩 타입은 바깥 타입에서 함께 처리, 패치 클래스는 위에서 처리
                try
                {
                    preparedMethods += PrecompileType(type, patchTypeSet);
                }
                catch (Exception ex)
                {
                    helperFailures.Add($"{type.Name}: {DescribeRoot(ex)}");
                    if (type == typeof(OfflineStubPatches)) offlineHelperFailed = true;
                }
            }
            sw.Stop();

            if (helperFailures.Count == 0)
                Mod.Log.Msg($"[Patch] 사전 JIT 점검 통과 — 메서드 {preparedMethods}개 (패치 적용 포함 {sw.ElapsedMilliseconds}ms)");
            else
                Mod.Log.Warning($"[Patch] 사전 JIT 점검에서 도우미 {helperFailures.Count}곳이 게임 interop과 맞지 않습니다(해당 기능 오동작 가능): " +
                                string.Join(" | ", helperFailures));
            if (offlineHelperFailed && SaveCustomConfig.OfflineMode)
                Mod.Log.Error("[Patch] 오프라인 스텁 도우미가 게임 interop과 맞지 않습니다 — 고치기 전에는 온라인 계정으로 플레이하지 마세요.");
        }

        /// <summary>
        /// 타입(과 그 안의 람다·중첩 타입)의 메서드를 전부 JIT해, 게임 업데이트로 사라진 interop 멤버 참조를 지금 드러낸다.
        /// 첫 실패에서 예외를 던진다. 제네릭 정의·추상·대리자 타입처럼 JIT할 본문이 없는 것은 건너뛴다.
        /// </summary>
        private static int PrecompileType(Type type, HashSet<Type> skip = null)
        {
            if (type.ContainsGenericParameters || typeof(Delegate).IsAssignableFrom(type) || type.IsInterface)
                return 0;

            int count = 0;
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (var m in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                if (m.IsAbstract || m.ContainsGenericParameters)
                    continue;
                var impl = m.GetMethodImplementationFlags();
                if ((impl & (MethodImplAttributes.InternalCall | MethodImplAttributes.Runtime)) != 0)
                    continue;
                try
                {
                    RuntimeHelpers.PrepareMethod(m.MethodHandle);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"{type.Name}.{m.Name} JIT 실패 — {DescribeRoot(ex)}", ex);
                }
                count++;
            }

            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (skip != null && skip.Contains(nested))
                    continue;
                count += PrecompileType(nested, skip);
            }
            return count;
        }

        private static string DescribeRoot(Exception ex)
        {
            var root = ex;
            while (root.InnerException != null) root = root.InnerException;
            return root == ex ? $"{ex.GetType().Name}: {ex.Message}" : $"{ex.Message} ({root.GetType().Name})";
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
