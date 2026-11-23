using System;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using Il2CppPIUMobileStepDLL;
using UnityEngine;

namespace PIURiseCustom
{
    /// <summary>
    /// SaveCustomKey/config.txt 기반 플레이 옵션 패치:
    /// - AutoPlay, AllPerfect, BlockSave, MaxScore
    /// - 실시간 판정바용 판정 오차(ms) 수집
    /// (키뷰어는 InGameOverlayUI가 GameInputManager를 직접 읽는다. 예전 KeyInput 추적 훅은 미사용이라 제거)
    /// </summary>
    internal static class GameplayOptionPatches
    {
        internal static PlayBase ActivePlayBase;

        // 1. AllPerfect: 실제로 노트를 쳐서 Great(1)/Good(2)/Bad(3) 판정이 났을 때만 PERFECT(0)로 승격
        //    (노트가 없는 빈 입력 grade=-1 / note==null 을 건드리면 빈 버튼만 눌러도 PERFECT 콤보가 올라가는 버그 방지)
        [HarmonyPatch(typeof(PUMPPlayer), nameof(PUMPPlayer.JudgeNote))]
        internal static class PUMPPlayer_JudgeNote_AllPerfect_Patch
        {
            private static void Prefix(ref Note note, ref int grade)
            {
                if (SaveCustomConfig.AllPerfect && note != null && grade >= 1 && grade <= 3)
                    grade = 0;
            }
        }

        [HarmonyPatch(typeof(PUMPPlayer), nameof(PUMPPlayer.JudgeLine))]
        internal static class PUMPPlayer_JudgeLine_AllPerfect_Patch
        {
            private static void Prefix(PUMPPlayer __instance, ref int Grade, Position processingPos)
            {
                try
                {
                    if (__instance != null && processingPos != null && SaveCustomConfig.EnableJudgmentBar && Grade >= 0 && Grade <= 3)
                    {
                        var div = __instance.GetDiv(processingPos.Block);
                        if (div != null)
                        {
                            float noteMs = div.msStart + processingPos.Line * div.msPerLine;
                            float deltaMs = __instance.msCurTime - noteMs;
                            if (Mathf.Abs(deltaMs) <= 220f)
                                InGameOverlayUI.RecordJudgment(deltaMs, SaveCustomConfig.AllPerfect ? 0 : Grade, __instance.mTiming);
                        }
                    }
                }
                catch { }

                if (SaveCustomConfig.AllPerfect && Grade >= 1 && Grade <= 3)
                    Grade = 0;
            }
        }

        // 2. AutoPlay + 활성 PlayBase 캡처
        //    - 실제 오토플레이 트리거는 미사용 필드인 PUMPPlayer.AutoPlay(0xFC)가 아니라 DivAutoPlay(0xF8).
        //    - PlayBase.Update()가 매 프레임 초반에 DivAutoPlay = -1 로 리셋한 뒤 PUMPPlayer.DrawStep()을 호출하고
        //      직후 if (DivAutoPlay != -1) PlayBase.AutoPush(DivAutoPlay, ...) 를 실행하므로
        //      PUMPPlayer.DrawStep Prefix에서 매 프레임 DivAutoPlay = JUDGE.ePerfect(0) 로 주입한다.
        [HarmonyPatch(typeof(PlayBase), nameof(PlayBase.StartSong))]
        internal static class PlayBase_StartSong_AutoPlay_Patch
        {
            private static void Prefix(PlayBase __instance)
            {
                ActivePlayBase = __instance;

                if (!SaveCustomConfig.AutoPlay || __instance == null || __instance._player == null)
                    return;

                __instance._player.DivAutoPlay = (JUDGE)0;
                __instance._player.AutoPlay = (JUDGE)0;
                Mod.Log.Msg("[AutoPlay] PUMPPlayer.DivAutoPlay = (JUDGE)0 (PERFECT) 활성화");
            }
        }

        [HarmonyPatch(typeof(PUMPPlayer), nameof(PUMPPlayer.DrawStep))]
        internal static class PUMPPlayer_DrawStep_AutoPlay_Patch
        {
            private static void Prefix(PUMPPlayer __instance)
            {
                if (SaveCustomConfig.AutoPlay && __instance != null)
                    __instance.DivAutoPlay = (JUDGE)0;
            }
        }

        // AutoPlay 중 노트 판정 시 게임 공식 노트 키음(AudioCategory.Note) 재생
        private static string _cachedNoteSfxKey;
        private static float _lastNoteSfxTime = -1f;

        private static string ResolveNoteSfxKey(AudioManager am)
        {
            if (am == null)
                return null;

            string equipped = null;
            try { equipped = PlayerData.Instance?.PlayerEquipInfo.SoundSkin; } catch { }

            try
            {
                var buffers = am._buffers;
                if (buffers != null && buffers.ContainsKey(AudioCategory.Note))
                {
                    var noteDict = buffers[AudioCategory.Note];
                    if (noteDict != null)
                    {
                        if (!string.IsNullOrEmpty(equipped) && noteDict.ContainsKey(equipped))
                        {
                            _cachedNoteSfxKey = equipped;
                            return _cachedNoteSfxKey;
                        }

                        if (!string.IsNullOrEmpty(_cachedNoteSfxKey) && noteDict.ContainsKey(_cachedNoteSfxKey))
                            return _cachedNoteSfxKey;

                        foreach (var kv in noteDict)
                        {
                            if (!string.IsNullOrEmpty(kv.Key))
                            {
                                _cachedNoteSfxKey = kv.Key;
                                Mod.Log?.Msg($"[AutoPlay] 기본 노트 키음 자동 선택: {_cachedNoteSfxKey}");
                                return _cachedNoteSfxKey;
                            }
                        }
                    }
                }
            }
            catch { }

            return !string.IsNullOrEmpty(equipped) ? equipped : _cachedNoteSfxKey;
        }

        [HarmonyPatch(typeof(PUMPPlayer), nameof(PUMPPlayer.JudgeStep_PostProcess))]
        internal static class PUMPPlayer_JudgeStep_AutoPlaySfx_Patch
        {
            private static void Postfix(JUDGE __1, bool __3)
            {
                if (!SaveCustomConfig.AutoPlay)
                    return;

                // Miss 제외, 롱노트 유지 틱(__3 == true) 제외, 동시치기 15ms 중복 방지
                if ((int)__1 < 0 || (int)__1 > 3 || __3)
                    return;

                float now = Time.unscaledTime;
                if (now - _lastNoteSfxTime < 0.015f)
                    return;

                try
                {
                    var am = AudioManager.Instance;
                    string key = ResolveNoteSfxKey(am);
                    if (am != null && !string.IsNullOrEmpty(key))
                    {
                        _lastNoteSfxTime = now;
                        am.Play(AudioCategory.Note, key, 1f, true, false);
                    }
                }
                catch { }
            }
        }

        // 3. BlockSave: 곡 클리어 기록 로컬 저장 차단 (옵션/키설정은 정상 저장)
        [HarmonyPatch(typeof(PlayerData), nameof(PlayerData.SaveUserPlayResult))]
        internal static class PlayerData_SaveUserPlayResult_BlockSave_Patch
        {
            private static bool Prefix()
            {
                if (!SaveCustomConfig.BlockSave)
                    return true;

                Mod.Log.Msg("[BlockSave] PlayerData.SaveUserPlayResult() 차단됨");
                return false;
            }
        }

        // 4. MaxScore: 기본 1,000,000 만점을 config의 MaxScore 기준으로 비례 스케일링
        [HarmonyPatch(typeof(PUMPPlayer), nameof(PUMPPlayer.CalcNewBestScore))]
        internal static class PUMPPlayer_CalcNewBestScore_MaxScore_Patch
        {
            private static void Postfix(PUMPPlayer __instance)
            {
                ApplyMaxScoreScale(__instance);
            }
        }

        [HarmonyPatch(typeof(PUMPPlayer), nameof(PUMPPlayer.ACCalcNewBestScore))]
        internal static class PUMPPlayer_ACCalcNewBestScore_MaxScore_Patch
        {
            private static void Postfix(PUMPPlayer __instance)
            {
                ApplyMaxScoreScale(__instance);
            }
        }

        [HarmonyPatch(typeof(ScoreRoller), nameof(ScoreRoller.InitData))]
        internal static class ScoreRoller_InitData_MaxScore_Patch
        {
            private static void Prefix(ref string score)
            {
                if (SaveCustomConfig.MaxScore == 1000000 || string.IsNullOrWhiteSpace(score))
                    return;

                try
                {
                    // 쉼표 제거 후 숫자 파싱
                    string digits = score.Replace(",", "").Trim();
                    if (long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out long val))
                    {
                        // 이미 CalcNewBestScore에서 스케일링된 값이 1,000,000을 넘으면 중복 스케일 방지
                        if (val <= 1000000)
                        {
                            long scaled = (long)Math.Round(val * (SaveCustomConfig.MaxScore / 1000000.0));
                            score = scaled.ToString("N0", CultureInfo.InvariantCulture);
                        }
                    }
                }
                catch { }
            }
        }

        // 5. PERFECT GAME / FULL COMBO 연출(CoShowNoMiss)이 곡 중간에 뜨지 않도록 BGM 막바지까지 대기
        //    - state 0(초기 진입)은 원본대로 통과시켜 중복 실행 방지 플래그를 세우게 하고,
        //    - state >= 1(실제 연출 발동 직전)에서만 BGM 종료 3.5초 전까지 1프레임씩 유예한다.
        [HarmonyPatch]
        internal static class PlayBase_CoShowNoMiss_MoveNext_Patch
        {
            private static FieldInfo _stateField;
            private static FieldInfo _currentField;
            private static PropertyInfo _stateProp;
            private static PropertyInfo _currentProp;

            private static MethodBase TargetMethod()
            {
                foreach (var nt in typeof(PlayBase).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (nt.Name.Contains("CoShowNoMiss", StringComparison.OrdinalIgnoreCase))
                    {
                        var m = nt.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (m != null)
                        {
                            _stateField = nt.GetField("__1__state", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            _currentField = nt.GetField("__2__current", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            _stateProp = nt.GetProperty("__1__state", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            _currentProp = nt.GetProperty("__2__current", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            return m;
                        }
                    }
                }
                return null;
            }

            private static bool _loggedWaiting = false;

            private static bool Prefix(object __instance, ref bool __result)
            {
                try
                {
                    int state = -1;
                    if (_stateProp != null) state = (int)_stateProp.GetValue(__instance);
                    else if (_stateField != null) state = (int)_stateField.GetValue(__instance);

                    // state 0은 원본이 초기 플래그/WaitForSeconds를 세팅하도록 통과
                    if (state <= 0)
                    {
                        _loggedWaiting = false;
                        return true;
                    }

                    var mp = AudioManager.Instance?.MusicPlayer;
                    if (mp != null && !mp.IsPlaybackFinished())
                    {
                        double len = mp.GetLengthSeconds();
                        double cur = mp.GetPlaybackTimeSeconds();
                        // BGM 종료 5.5초 전까지 대기 (연출 애니메이션이 결과창 전환 전에 끝나도록)
                        if (len > 8.0 && (len - cur) > 5.5)
                        {
                            if (!_loggedWaiting)
                            {
                                _loggedWaiting = true;
                                Mod.Log?.Msg($"[ClearMark] PERFECT GAME 연출 유예 시작 ({cur:F1}s / {len:F1}s)");
                            }
                            if (_currentProp != null) _currentProp.SetValue(__instance, null);
                            else if (_currentField != null) _currentField.SetValue(__instance, null);

                            __result = true;
                            return false;
                        }
                    }

                    if (_loggedWaiting)
                    {
                        _loggedWaiting = false;
                        Mod.Log?.Msg("[ClearMark] BGM 막바지 도달 — PERFECT GAME 연출 재생");
                    }
                }
                catch { }

                return true;
            }
        }

        // 6. 부분 채보 테스트 중 마지막 노트가 지난 뒤에도 BGM이 남아있으면 Esc 일시정지 메뉴 허용
        //    (IL2CPP에서 InGameUI.callMenu 내부에 MainStage.get_PauseAllow가 인라인되므로
        //     InGameUI.callMenu를 직접 패치해 호출 중 SoloPlayer._showNoteEnd를 임시 해제한다.)
        private static bool ShouldAllowPauseDuringRemainingBgm(MainStage stage)
        {
            if (stage == null || stage._ready || stage._startSaveResult)
                return false;

            var mp = AudioManager.Instance?.MusicPlayer;
            if (mp == null || mp.IsPlaybackFinished())
                return false;

            double len = mp.GetLengthSeconds();
            double cur = mp.GetPlaybackTimeSeconds();
            return len > 5.0 && (len - cur) > 3.0;
        }

        [HarmonyPatch(typeof(MainStage), nameof(MainStage.PauseAllow), MethodType.Getter)]
        internal static class MainStage_PauseAllow_Patch
        {
            private static void Postfix(MainStage __instance, ref bool __result)
            {
                if (__result || __instance == null)
                    return;

                try
                {
                    if (ShouldAllowPauseDuringRemainingBgm(__instance))
                        __result = true;
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(InGameUI), "callMenu")]
        internal static class InGameUI_callMenu_AllowPause_Patch
        {
            private static void Prefix(bool value, ref bool __state)
            {
                __state = false;
                if (!value)
                    return;

                try
                {
                    var stage = MainStage.Instance;
                    var solo = stage?.SoloPlayer;
                    if (solo != null && solo._showNoteEnd && ShouldAllowPauseDuringRemainingBgm(stage))
                    {
                        solo._showNoteEnd = false;
                        __state = true;
                    }
                }
                catch { }
            }

            private static void Postfix(bool __state)
            {
                if (!__state)
                    return;

                try
                {
                    var solo = MainStage.Instance?.SoloPlayer;
                    if (solo != null)
                        solo._showNoteEnd = true;
                }
                catch { }
            }
        }

        private static void ApplyMaxScoreScale(PUMPPlayer player)
        {
            if (SaveCustomConfig.MaxScore == 1000000 || player == null)
                return;

            try
            {
                var statObj = player.Stat;
                if (statObj == null) return;

                var arr = statObj.TryCast<Il2CppSystem.Array>();
                if (arr == null) return;

                // Il2Cpp 2D 배열 _stat[0, (int)Count.eCountScore(13)]
                var rawObj = arr.GetValue(0, 13);
                if (rawObj == null) return;

                int rawScore = rawObj.Unbox<int>();
                if (rawScore <= 0) return;

                double scale = SaveCustomConfig.MaxScore / 1000000.0;
                int scaled = (int)Math.Round(rawScore * scale);
                arr.SetValue(Il2CppSystem.Int32.Parse(scaled.ToString(CultureInfo.InvariantCulture)), 0, 13);
            }
            catch { }
        }
    }
}
