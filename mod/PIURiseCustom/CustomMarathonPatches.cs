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

        public static void OnCustomSongStarted(string songId)
        {
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
            if (!SaveCustomConfig.CustomMarathon || _activeResultUI == null || _transitionTriggered)
            {
                DestroyBanner();
                return;
            }

            UpdateBannerUI();

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Mod.Log?.Msg("[Marathon] Esc 입력 — 마라톤 종료");
                _playedIds.Clear();
                _activeResultUI = null;
                DestroyBanner();
                return;
            }

            bool triggerNext = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            if (!triggerNext && SaveCustomConfig.CustomMarathonAutoNext > 0f && _resultShownTime > 0f)
            {
                if (Time.unscaledTime - _resultShownTime >= SaveCustomConfig.CustomMarathonAutoNext)
                    triggerNext = true;
            }

            if (triggerNext)
                StartNextMarathonSong();
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
                    return;

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
                        float rem = Mathf.Max(0f, SaveCustomConfig.CustomMarathonAutoNext - (Time.unscaledTime - _resultShownTime));
                        timerStr = $" ({rem:0.0}s)";
                    }

                    _bannerTmp.text = $"MARATHON {_playedIds.Count}/{CustomPackManager.CatalogSongs.Count} | Enter: Next{timerStr} | Esc: Stop";
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
                if (!SaveCustomConfig.CustomMarathon)
                    return;

                if (!Util.IsCurrentSongCustom())
                    return;

                _activeResultUI = __instance;
                _resultShownTime = Time.unscaledTime;
                _transitionTriggered = false;
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
