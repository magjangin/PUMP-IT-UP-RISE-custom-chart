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
                            if (Math.Abs(deltaMs) <= 220f) // Mathf.Abs는 IL2CPP 호출이라 판정마다 부르지 않는다
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
                    // 키음 이름은 곡마다 한 번만 찾는다(v0.3.6). 예전엔 판정마다 장착 스킨 조회 + IL2CPP 사전 ContainsKey 2~3회를 반복했다.
                    // 못 찾았으면(버퍼가 아직 안 올라옴) 다음 판정에서 다시 찾는다.
                    string key = _stageNoteSfxKey;
                    if (string.IsNullOrEmpty(key))
                        key = _stageNoteSfxKey = ResolveNoteSfxKey(am);
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

        // 4. MaxScore: 결과 화면이 점수를 찍는 NumberRoller.RollCombo(Score, int value, float startDelay)의 value를 config의 MaxScore 기준으로 비례 스케일링한다.
        //    - v0.3.0~v0.3.1: CalcNewBestScore/ACCalcNewBestScore Postfix에서 _stat[0,13]을 곱했지만, 두 메서드는 저장된 기록과 비교하는 "신기록 판정"일 뿐
        //      점수를 쓰지 않아(BlockSave 때는 호출조차 안 됨) MaxScore가 전혀 적용되지 않았다.
        //    - v0.3.2 1차: PlayBase.GetScoreAndAccuracy를 후킹했지만 실기 로그에서 한 번도 불리지 않았다(결과 화면은 이 경로를 안 씀).
        //    - 실기 로그(2026-11-23)로 확인된 실제 경로: NumberRoller.RollCombo(Score, 1000000) → 여기서 스케일한다.
        //    표시값만 바꾸고 게임 내부 점수(_stat)는 건드리지 않으며, 점수를 스케일하는 곳은 이 한 군데뿐이라 이중 적용이 없다.
        [HarmonyPatch(typeof(NumberRoller), nameof(NumberRoller.RollCombo), new[] { typeof(NumberRoller.StatType), typeof(int), typeof(float) })]
        internal static class NumberRoller_RollCombo_MaxScore_Patch
        {
            private static void Prefix(NumberRoller.StatType stat, ref int value)
            {
                if (stat != NumberRoller.StatType.Score)
                    return;

                try
                {
                    int max = SaveCustomConfig.MaxScore;
                    int raw = value;
                    if (max != 1000000 && raw > 0)
                        value = (int)Math.Round(raw * (max / 1000000.0));

                    LogScorePath($"NumberRoller.RollCombo(Score) 표시값 {raw} → {value} (MaxScore={max})");
                }
                catch (Exception ex)
                {
                    LogScorePath($"RollCombo 점수 처리 오류: {ex.Message}");
                }
            }
        }

        // 확인용(값은 바꾸지 않음): 다른 화면·모드가 아래 경로로 점수를 표시하는지 기록한다. 로그에 뜨면 해당 경로에도 스케일이 필요하다는 뜻이다.
        [HarmonyPatch(typeof(ArcadeResultUI), nameof(ArcadeResultUI.ApplyScoreCoroutine))]
        internal static class ArcadeResultUI_ApplyScoreCoroutine_Log_Patch
        {
            private static void Prefix(int score) => LogScorePath($"[미처리 경로] ArcadeResultUI.ApplyScoreCoroutine(score={score})");
        }

        // PlayBase.GetScoreAndAccuracy 로그 훅은 v0.3.4에서 제거했다: 실기에서 한 번도 불리지 않았고,
        // 반환형이 값 형식(ValueTuple<int,float>)이라 ConnectAsync(CancellationToken) 크래시와 같은 박싱 위험이 있다(AGENTS.md §5).

        // 점수 경로 로그는 스테이지당 처음 12줄만 남긴다(프레임마다 불리는 경로여도 로그가 넘치지 않게).
        private static int _scorePathLogCount;

        // 곡이 바뀌면 장착 키음 스킨이 바뀌었을 수 있으므로 다시 찾게 한다
        private static string _stageNoteSfxKey;

        internal static void ResetForNewStage()
        {
            _scorePathLogCount = 0;
            _stageNoteSfxKey = null;
        }

        private static void LogScorePath(string msg)
        {
            if (_scorePathLogCount >= 12) return;
            _scorePathLogCount++;
            Mod.Log.Msg($"[MaxScore] {msg} ({_scorePathLogCount}/12)");
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
    }
}
