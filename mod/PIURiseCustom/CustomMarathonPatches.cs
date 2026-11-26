using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PIURiseCustom
{
    /// <summary>
    /// SaveCustomKey/config.txt 기반 커스텀 곡 마라톤 (CustomMarathon, CustomMarathonAutoNext).
    /// - 커스텀 곡을 시작하면 마라톤 세션이 시작되고, 결과창에서 Enter(또는 자동 타이머)로 아직 안 친 랜덤 커스텀 곡을 바로 시작한다.
    /// - 결과창에서 Esc를 누르면 마라톤을 종료하고 선곡 화면으로 돌아간다.
    /// </summary>
    internal static class CustomMarathonPatches
    {
        private static readonly HashSet<string> _playedIds = new(StringComparer.OrdinalIgnoreCase);
        private static readonly System.Random _rng = new();

        private static ResultUI _activeResultUI;
        private static float _resultShownTime = -1f;
        private static bool _transitionTriggered = false;

        // uGUI 마라톤 배너 (IL2CPP에서 스트립된 OnGUI 미사용)
        private static GameObject _bannerCanvasObj;
        private static TextMeshProUGUI _bannerTmp;
        private static string _lastBannerText;

        public static void OnCustomSongStarted(string songId)
        {
            // 곡이 시작됐으면 직전 결과창 전환은 끝난 것이다. 예전엔 마라톤 전환 뒤 일시정지→나가기로 결과창을 거치지 않으면
            // 이 값이 true로 남아, 이후 모든 결과창(공식 곡 포함)에서 Esc(DoEscape)가 막혔다.
            _transitionTriggered = false;
            _activeResultUI = null;

            if (!SaveCustomConfig.CustomMarathon || string.IsNullOrEmpty(songId))
                return;

            if (!CustomPackManager.IsCatalogSongId(songId))
            {
                _playedIds.Clear();
                return;
            }

            _playedIds.Add(songId);
            Mod.Log?.Msg($"[Marathon] 진행 중: {_playedIds.Count} / {CustomPackManager.CatalogSongs.Count} ({songId})");
        }

        private static bool HasRemainingCustomSongs()
        {
            foreach (var p in CustomPackManager.CatalogSongs)
            {
                if (!_playedIds.Contains(p.SongId))
                    return true;
            }
            return false;
        }

        private static CustomPack PickNextRandomSong()
        {
            var pool = new List<CustomPack>();
            foreach (var p in CustomPackManager.CatalogSongs)
            {
                if (!_playedIds.Contains(p.SongId))
                    pool.Add(p);
            }
            if (pool.Count == 0) return null;
            return pool[_rng.Next(pool.Count)];
        }

        public static void OnUpdate()
        {
            // 결과창 참조는 우리가 null로 비우므로 관리 비교(is null)를 먼저 해 평소엔 IL2CPP 호출(Unity == null)을 하지 않는다.
            // 마라톤 결과창이 떠 있을 때만 파괴 여부를 Unity 비교로 확인한다.
            if (!SaveCustomConfig.CustomMarathon || _activeResultUI is null || _transitionTriggered || _activeResultUI == null)
            {
                // 배너를 만든 적 없으면 매 프레임 Unity null 비교(IL2CPP 호출)도 하지 않는다
                if (_bannerCanvasObj is not null)
                    DestroyBanner();
                return;
            }

            UpdateBannerUI();

            if (MarathonKeys.EscapePressed())
            {
                Mod.Log?.Msg("[Marathon] Esc 입력 — 마라톤 종료");
                _playedIds.Clear();
                _activeResultUI = null;
                DestroyBanner();
                return;
            }

            bool triggerNext = MarathonKeys.EnterPressed();
            if (!triggerNext && SaveCustomConfig.CustomMarathonAutoNext > 0f && _resultShownTime > 0f)
            {
                if (Time.unscaledTime - _resultShownTime >= SaveCustomConfig.CustomMarathonAutoNext)
                    triggerNext = true;
            }

            if (triggerNext)
                StartNextMarathonSong();
        }

        /// <summary>
        /// 결과창 Enter/Esc 읽기 (v0.3.15). 게임 입력은 Unity InputSystem(GameInputManager)이라, 플레이어 설정이 "Input System 전용"이면
        /// 구식 UnityEngine.Input은 호출할 때마다 InvalidOperationException을 던진다(예전엔 Mod.OnUpdate의 WarnOnce에 걸려 마라톤이 통째로 멈춤).
        /// 그래서 InputSystem의 Keyboard.current를 먼저 쓰고, 그게 안 되면(키보드 없음·API 변경) 구식 Input으로 내려간다.
        /// 둘 다 실패하면 경고 1회 후 키 입력만 끄고 자동 진행 타이머(CustomMarathonAutoNext)는 계속 동작한다.
        /// </summary>
        private static class MarathonKeys
        {
            private static bool _inputSystemFailed, _legacyFailed;

            internal static bool EscapePressed() => Read(escape: true);
            internal static bool EnterPressed() => Read(escape: false);

            private static bool Read(bool escape)
            {
                if (!_inputSystemFailed)
                {
                    try
                    {
                        var kb = UnityEngine.InputSystem.Keyboard.current;
                        if (kb is not null)
                            return escape
                                ? kb.escapeKey.wasPressedThisFrame
                                : kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
                    }
                    catch (Exception ex)
                    {
                        _inputSystemFailed = true;
                        Mod.Log?.Warning($"[Marathon] InputSystem 키보드 읽기 실패 → 구식 Input으로 전환: {ex.GetType().Name}: {ex.Message}");
                    }
                }

                if (_legacyFailed)
                    return false;
                try
                {
                    return escape
                        ? Input.GetKeyDown(KeyCode.Escape)
                        : Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
                }
                catch (Exception ex)
                {
                    _legacyFailed = true;
                    Mod.Log?.Warning($"[Marathon] 키 입력을 읽을 수 없어 Enter/Esc를 끕니다(자동 진행 타이머는 동작): {ex.GetType().Name}: {ex.Message}");
                    return false;
                }
            }
        }

        private static void StartNextMarathonSong()
        {
            var next = PickNextRandomSong();
            if (next == null)
            {
                Mod.Log?.Msg("[Marathon] 모든 커스텀 곡 완주 완료!");
                _playedIds.Clear();
                _activeResultUI = null;
                return;
            }

            try
            {
                var db = PIUDataBase.Instance;
                var pd = PlayerData.Instance;
                if (db == null || pd == null || db.songTable == null || !db.songTable.ContainsKey(next.SongId))
                {
                    // 예전엔 그냥 돌아가서, 자동 진행 타이머가 켜져 있으면 이 실패를 매 프레임 조용히 다시 시도했다(v0.3.11)
                    Mod.Log?.Warning($"[Marathon] 다음 곡 {next.SongId}({next.Title})을 곡 표에서 찾지 못해 마라톤을 멈춥니다");
                    _activeResultUI = null;
                    DestroyBanner();
                    return;
                }

                _transitionTriggered = true;
                var song = db.songTable[next.SongId];
                pd.Song = song;
                int lv = next.SingleLevel > 0 ? next.SingleLevel : 10;
                pd.SelectedLevel = lv;
                SongTableInjectorPatches.LastCustomSelectedLevel = lv;

                Mod.Log?.Msg($"[Marathon] 다음 곡 자동 진입: {next.Title} ({next.SongId})");
                _activeResultUI.DoReplay();
                _activeResultUI = null;
            }
            catch (Exception ex)
            {
                Mod.Log?.Warning($"[Marathon] 다음 곡 전환 실패: {ex.Message}");
                _transitionTriggered = false;
            }
        }

        private static void DestroyBanner()
        {
            if (_bannerCanvasObj != null)
            {
                try { UnityEngine.Object.Destroy(_bannerCanvasObj); } catch { }
            }
            _bannerCanvasObj = null;
            _bannerTmp = null;
            _lastBannerText = null;
        }

        private static void UpdateBannerUI()
        {
            if (!HasRemainingCustomSongs())
            {
                DestroyBanner();
                return;
            }

            try
            {
                if (_bannerCanvasObj == null)
                {
                    _bannerCanvasObj = new GameObject("PIURiseCustom_MarathonBanner");
                    var canvas = _bannerCanvasObj.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.sortingOrder = 10000;

                    var scaler = _bannerCanvasObj.AddComponent<CanvasScaler>();
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920f, 1080f);

                    var bgObj = new GameObject("BG");
                    bgObj.transform.SetParent(_bannerCanvasObj.transform, false);
                    var bgImg = bgObj.AddComponent<Image>();
                    bgImg.color = new Color(0.06f, 0.08f, 0.14f, 0.88f);
                    bgImg.raycastTarget = false;
                    var bgRt = bgImg.rectTransform;
                    bgRt.anchorMin = new Vector2(0.5f, 1f);
                    bgRt.anchorMax = new Vector2(0.5f, 1f);
                    bgRt.pivot = new Vector2(0.5f, 1f);
                    bgRt.anchoredPosition = new Vector2(0f, -20f);
                    bgRt.sizeDelta = new Vector2(680f, 44f);

                    var txtObj = new GameObject("Label");
                    txtObj.transform.SetParent(bgObj.transform, false);
                    _bannerTmp = txtObj.AddComponent<TextMeshProUGUI>();
                    _bannerTmp.alignment = TextAlignmentOptions.Center;
                    _bannerTmp.fontSize = 22f;
                    _bannerTmp.color = Color.white;
                    _bannerTmp.raycastTarget = false;
                    var existingTmp = UnityEngine.Object.FindObjectOfType<TextMeshProUGUI>();
                    if (existingTmp != null && existingTmp.font != null)
                        _bannerTmp.font = existingTmp.font;
                    var txtRt = _bannerTmp.rectTransform;
                    txtRt.anchorMin = Vector2.zero;
                    txtRt.anchorMax = Vector2.one;
                    txtRt.offsetMin = Vector2.zero;
                    txtRt.offsetMax = Vector2.zero;
                }

                if (_bannerTmp != null)
                {
                    string timerStr = "";
                    if (SaveCustomConfig.CustomMarathonAutoNext > 0f && _resultShownTime > 0f)
                    {
                        float rem = MathF.Max(0f, SaveCustomConfig.CustomMarathonAutoNext - (Time.unscaledTime - _resultShownTime));
                        timerStr = $" ({rem:0.0}s)";
                    }

                    string text = $"MARATHON {_playedIds.Count}/{CustomPackManager.CatalogSongs.Count} | Enter: Next{timerStr} | Esc: Stop";
                    // 텍스트가 바뀐 프레임에만 TMP에 대입해 매 프레임 문자열 재파싱·메쉬 재빌드를 피한다
                    if (!string.Equals(text, _lastBannerText, StringComparison.Ordinal))
                    {
                        _lastBannerText = text;
                        _bannerTmp.text = text;
                    }
                }
            }
            catch { }
        }

        [HarmonyPatch(typeof(ResultUI), "DoEscape")]
        internal static class ResultUI_DoEscape_Marathon_Patch
        {
            private static bool Prefix()
            {
                if (_transitionTriggered)
                    return false;
                return true;
            }
        }

        [HarmonyPatch(typeof(ResultUI), nameof(ResultUI.OnShow), typeof(Il2CppReferenceArray<Il2CppSystem.Object>))]
        internal static class ResultUI_OnShow_Marathon_Patch
        {
            private static void Postfix(ResultUI __instance)
            {
                InGameOverlayUI.IsInStage = false;
                _transitionTriggered = false; // 새 결과창이 떴으면 이전 전환은 끝났다(공식 곡 결과창에서도 ESC가 막히지 않게)
                if (!SaveCustomConfig.CustomMarathon)
                    return;

                if (!Util.IsCurrentSongCustom())
                    return;

                _activeResultUI = __instance;
                _resultShownTime = Time.unscaledTime;
            }
        }

        [HarmonyPatch(typeof(ResultUI), nameof(ResultUI.OnHide))]
        internal static class ResultUI_OnHide_Marathon_Patch
        {
            private static void Postfix()
            {
                _activeResultUI = null;
                _resultShownTime = -1f;
                DestroyBanner();
            }
        }
    }
}
