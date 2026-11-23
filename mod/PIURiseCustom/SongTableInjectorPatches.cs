using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Il2Cpp;
using Il2CppData;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace PIURiseCustom
{
    /// <summary>
    /// hwa/ 하위 폴더의 커스텀 곡들을 PIUDataBase.songTable에 독립 곡(ID 990001...)으로 등록하고,
    /// 선곡 화면 자켓(EyecatchSprite)을 폴더의 cover.png / Thumbnail.png 등으로 교체한다.
    /// </summary>
    internal static class SongTableInjectorPatches
    {
        // 커스텀 곡(990001…)을 GAMESTART·PlayBase.LoadStep·방 생성 창에 넘길 때 원본 DataAssets/Addressables 검사 통과용으로 쓰는 공식 템플릿 곡 정보
        // (songTable 주입 때 처음 만난 공식 곡으로 정해진다. 공식 곡이 하나도 없으면 null → 커스텀 곡 시작 불가)
        internal static string TemplateOfficialSongId;
        internal static int TemplateOfficialLevel = 1;
        internal static string TemplateOfficialPlayType = "Single";

        // 마지막으로 선택한 커스텀 곡의 원래 난이도 (로딩 화면/인게임 UI 표시 복원용)
        internal static int LastCustomSelectedLevel = 10;

        private static readonly Dictionary<string, Sprite> _jacketCache = new(StringComparer.OrdinalIgnoreCase);
        // 디코딩에 실패한 이미지 경로. 선곡 리스트 스크롤마다(UpdateDisplayContent) 깨진 파일을 다시 읽어 프레임이 끊기는 것을 막는다.
        private static readonly HashSet<string> _jacketFailed = new(StringComparer.OrdinalIgnoreCase);

        private static void InjectIntoTable(Il2CppSystem.Collections.Generic.Dictionary<string, Song> table, string tableName)
        {
            if (!ModConfig.CustomEnabled.Value || table == null)
                return;

            var songs = CustomPackManager.CatalogSongs;
            if (songs.Count == 0)
                return;

            // 공식 곡 중 하나를 찾아 Channel/Generation 기본값 및 템플릿 ID 확보
            Song template = null;
            foreach (var kv in table)
            {
                if (!CustomPackManager.IsCatalogSongId(kv.Key))
                {
                    template = kv.Value;
                    if (TemplateOfficialSongId == null)
                    {
                        TemplateOfficialSongId = kv.Key;
                        try
                        {
                            var levels = kv.Value.Levels;
                            if (levels != null && levels.ContainsKey(Song.STEP.Single))
                            {
                                var sList = levels[Song.STEP.Single];
                                if (sList != null && sList.Count > 0)
                                {
                                    TemplateOfficialLevel = sList[0];
                                    TemplateOfficialPlayType = "Single";
                                }
                            }
                        }
                        catch { }
                    }
                    break;
                }
            }

            int defaultChannel = template != null ? template.Channel : 1;
            int defaultGen = template != null ? template.Generation : 0;
            int defaultLenType = template != null ? template.LengthType : 0;
            string defaultImgPath = template != null ? template.IcathImgPath : "";

            int injected = 0;
            foreach (var pack in songs)
            {
                try
                {
                    var levelsDict = new Il2CppSystem.Collections.Generic.Dictionary<Song.STEP, Il2CppSystem.Collections.Generic.List<int>>();
                    var hiddensDict = new Il2CppSystem.Collections.Generic.Dictionary<Song.STEP, Il2CppSystem.Collections.Generic.List<int>>();

                    int sLv = pack.SingleLevel > 0 ? pack.SingleLevel : 10;
                    int dLv = pack.DoubleLevel > 0 ? pack.DoubleLevel : sLv;

                    var singleList = new Il2CppSystem.Collections.Generic.List<int>();
                    if (pack.SingleLevels != null && pack.SingleLevels.Count > 0)
                    {
                        for (int i = 0; i < pack.SingleLevels.Count; i++)
                            singleList.Add(pack.SingleLevels[i]);
                    }
                    else
                    {
                        singleList.Add(sLv);
                    }
                    levelsDict[Song.STEP.Single] = singleList;

                    var srcDouble = (pack.DoubleLevels != null && pack.DoubleLevels.Count > 0)
                        ? pack.DoubleLevels
                        : (pack.SingleLevels != null && pack.SingleLevels.Count > 0 ? pack.SingleLevels : null);

                    var halfDoubleList = new Il2CppSystem.Collections.Generic.List<int>();
                    var doubleList = new Il2CppSystem.Collections.Generic.List<int>();
                    if (srcDouble != null && srcDouble.Count > 0)
                    {
                        for (int i = 0; i < srcDouble.Count; i++)
                        {
                            halfDoubleList.Add(srcDouble[i]);
                            doubleList.Add(srcDouble[i]);
                        }
                    }
                    else
                    {
                        halfDoubleList.Add(dLv);
                        doubleList.Add(dLv);
                    }
                    levelsDict[Song.STEP.HalfDouble] = halfDoubleList;
                    levelsDict[Song.STEP.Double] = doubleList;

                    hiddensDict[Song.STEP.Single] = new Il2CppSystem.Collections.Generic.List<int>();
                    hiddensDict[Song.STEP.Double] = new Il2CppSystem.Collections.Generic.List<int>();
                    hiddensDict[Song.STEP.HalfDouble] = new Il2CppSystem.Collections.Generic.List<int>();

                    var customSong = new Song
                    {
                        ParentID = pack.SongId,
                        ID = pack.SongId,
                        Bpm = string.IsNullOrEmpty(pack.BpmText) ? "120" : pack.BpmText,
                        TitleEng = pack.Title,
                        TitleKor = pack.Title,
                        ArtistEng = pack.Artist,
                        ArtistKor = pack.Artist,
                        Channel = defaultChannel,
                        LockValue = 0,
                        LengthType = defaultLenType,
                        Generation = defaultGen,
                        IcathImgPath = defaultImgPath,
                        Levels = levelsDict,
                        Hiddens = hiddensDict
                    };

                    table[pack.SongId] = customSong;
                    injected++;
                }
                catch (Exception ex)
                {
                    Mod.Log.Warning($"[SongInjector] {pack.SongId} ({pack.Title}) 등록 실패: {ex.Message}");
                }
            }

            Mod.Log.Msg($"[SongInjector] {tableName}에 hwa 커스텀 곡 {injected}/{songs.Count}개 등록 완료 (템플릿={TemplateOfficialSongId} {TemplateOfficialPlayType} Lv.{TemplateOfficialLevel})");
        }

        [HarmonyPatch(typeof(Mk2SongLoader), nameof(Mk2SongLoader.CacheSongTable))]
        internal static class Mk2SongLoader_CacheSongTable_Patch
        {
            private static void Prefix()
            {
                // Prefix 예외는 원본 CacheSongTable까지 건너뛰게 만들어 공식 곡 목록이 비므로 반드시 격리
                try
                {
                    var db = PIUDataBase.Instance;
                    if (db != null)
                        InjectIntoTable(db.songTable, "songTable");
                }
                catch (Exception ex)
                {
                    Mod.Log.Error($"[SongInjector] songTable 주입 실패(공식 곡은 정상 유지): {ex}");
                }
            }

            private static void Postfix(Mk2SongLoader __instance)
            {
                try
                {
                    int total = __instance._contents != null ? __instance._contents.Count : -1;
                    int allTunes = (__instance._channelCache != null && __instance._channelCache.ContainsKey(Il2CppPIUUtil.CommonEnums.Channel.ALLTUNES))
                        ? __instance._channelCache[Il2CppPIUUtil.CommonEnums.Channel.ALLTUNES].Count
                        : -1;
                    Mod.Log.Msg($"[SongInjector] CacheSongTable 완료: _contents={total}, ALLTUNES={allTunes}");
                }
                catch (Exception ex)
                {
                    Mod.Log.Warning($"[SongInjector] CacheSongTable 진단 로그 오류: {ex.Message}");
                }
            }
        }

        [HarmonyPatch(typeof(Mk2SongLoader), nameof(Mk2SongLoader.CacheArcadeSongTable))]
        internal static class Mk2SongLoader_CacheArcadeSongTable_Patch
        {
            private static void Prefix()
            {
                try
                {
                    var db = PIUDataBase.Instance;
                    if (db != null)
                        InjectIntoTable(db.acSongTable, "acSongTable");
                }
                catch (Exception ex)
                {
                    Mod.Log.Error($"[SongInjector] acSongTable 주입 실패(공식 곡은 정상 유지): {ex}");
                }
            }
        }

        internal static Sprite LoadOrGetJacket(string jacketPath)
        {
            if (string.IsNullOrEmpty(jacketPath))
                return null;

            // 캐시를 디스크 확인(File.Exists)보다 먼저 본다 — 이 함수는 리스트 셀 갱신마다 불린다
            if (_jacketCache.TryGetValue(jacketPath, out var cached) && cached != null)
                return cached;

            if (_jacketFailed.Contains(jacketPath) || !File.Exists(jacketPath))
                return null;

            try
            {
                byte[] bytes = File.ReadAllBytes(jacketPath);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                bool ok = ImageConversion.LoadImage(tex, new Il2CppStructArray<byte>(bytes), false);
                if (!ok)
                {
                    _jacketFailed.Add(jacketPath);
                    Mod.Log.Warning($"[CustomJacket] ImageConversion.LoadImage 실패 ({jacketPath}, {bytes.Length} bytes) — 재시작 전까지 다시 시도하지 않음");
                    return null;
                }

                var sprite = Sprite.Create(
                    tex,
                    new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    100f);

                sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                _jacketCache[jacketPath] = sprite;
                Mod.Log.Msg($"[CustomJacket] 자켓 로드 성공: {Path.GetFileName(jacketPath)} ({tex.width}x{tex.height})");
                return sprite;
            }
            catch (Exception ex)
            {
                _jacketFailed.Add(jacketPath);
                Mod.Log.Warning($"[CustomJacket] 로드 실패 ({jacketPath}): {ex.Message}");
                return null;
            }
        }

        [HarmonyPatch(typeof(SongDataCell), nameof(SongDataCell.Init), typeof(string), typeof(byte), typeof(int))]
        internal static class SongDataCell_Init_Patch
        {
            private static void Postfix(SongDataCell __instance, string songID)
            {
                if (!ModConfig.CustomEnabled.Value || __instance == null || __instance._eyeCatch == null)
                    return;

                var pack = CustomPackManager.ResolveBySongId(songID);
                if (pack == null || string.IsNullOrEmpty(pack.JacketPath))
                    return;

                var sprite = LoadOrGetJacket(pack.JacketPath);
                if (sprite != null)
                    __instance._eyeCatch.sprite = sprite;
            }
        }

        // 선곡 화면 오른쪽 리스트 셀(MSListBox) 자켓 썸네일 직접 주입
        [HarmonyPatch(typeof(Il2CppAirFishLab.ScrollingList.MSListBox), nameof(Il2CppAirFishLab.ScrollingList.MSListBox.UpdateDisplayContent))]
        internal static class MSListBox_UpdateDisplayContent_Patch
        {
            private static void Postfix(Il2CppAirFishLab.ScrollingList.MSListBox __instance)
            {
                ApplyCustomJacketToBox(__instance);
            }
        }

        [HarmonyPatch(typeof(Il2CppAirFishLab.ScrollingList.MSListBox), nameof(Il2CppAirFishLab.ScrollingList.MSListBox.UpdateLevels))]
        internal static class MSListBox_UpdateLevels_Patch
        {
            private static void Postfix(Il2CppAirFishLab.ScrollingList.MSListBox __instance)
            {
                ApplyCustomJacketToBox(__instance);
            }
        }

        private static void ApplyCustomJacketToBox(Il2CppAirFishLab.ScrollingList.MSListBox box)
        {
            if (!ModConfig.CustomEnabled.Value || box == null || box._eyecatchImage == null)
                return;

            var content = box.Content;
            if (content == null)
                return;

            var pack = CustomPackManager.ResolveBySongId(Util.SongId(content.Song));
            if (pack == null || string.IsNullOrEmpty(pack.JacketPath))
                return;

            var sprite = LoadOrGetJacket(pack.JacketPath);
            if (sprite != null)
            {
                box._eyecatchImage.sprite = sprite;
                box._eyecatchImage.color = Color.white;
            }
        }

        // 커스텀 곡은 잠금 검사 무조건 통과
        [HarmonyPatch(typeof(MainStation), nameof(MainStation.CheckSongLock))]
        internal static class MainStation_CheckSongLock_Patch
        {
            private static bool Prefix(string songID, ref bool __result)
            {
                if (ModConfig.CustomEnabled.Value && CustomPackManager.IsCatalogSongId(songID))
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        // 곡 시작 진입: 커스텀 곡은 GAMESTART의 파일/Addressables 커튼 검사를 템플릿 공식 곡으로 통과시킨 뒤
        // Postfix에서 PlayerData.Instance.Song, SelectedLevel, Curtain을 즉시 커스텀 곡으로 복원한다.
        [HarmonyPatch(typeof(MainStation), nameof(MainStation.GAMESTART))]
        internal static class MainStation_GAMESTART_Patch
        {
            internal sealed class GameStartState
            {
                public string OrigSongId;
                public int OrigLevel;
            }

            private static void Prefix(ref string songID, ref string playType, ref int playLevel, ref Song.STEP currentStep, out GameStartState __state)
            {
                __state = null;
                // 관문 Prefix: 여기서 예외가 새면 원본 GAMESTART가 건너뛰어져 공식 곡까지 시작되지 않으므로 격리한다(v0.3.4)
                try
                {
                    PrefixCore(ref songID, ref playType, ref playLevel, ref currentStep, ref __state);
                }
                catch (Exception ex)
                {
                    __state = null;
                    Mod.Log.Warning($"[MainStation.GAMESTART] 전처리 오류(원본 그대로 진행): {ex.Message}");
                }
            }

            private static void PrefixCore(ref string songID, ref string playType, ref int playLevel, ref Song.STEP currentStep, ref GameStartState __state)
            {
                Mod.Log.Msg($"[MainStation.GAMESTART] songID={songID}, playType={playType}, lv={playLevel}, step={currentStep}");

                if (ModConfig.CustomEnabled.Value &&
                    CustomPackManager.IsCatalogSongId(songID) &&
                    !string.IsNullOrEmpty(TemplateOfficialSongId))
                {
                    __state = new GameStartState
                    {
                        OrigSongId = songID,
                        OrigLevel = playLevel
                    };
                    LastCustomSelectedLevel = playLevel > 0 ? playLevel : 10;
                    songID = TemplateOfficialSongId;
                    playType = TemplateOfficialPlayType;
                    playLevel = TemplateOfficialLevel;
                    currentStep = Song.STEP.Single;
                    Mod.Log.Msg($"[MainStation.GAMESTART] 커스텀 곡({__state.OrigSongId} Lv.{__state.OrigLevel}) → 템플릿({songID} {playType} Lv.{playLevel}) 경유 씬 전환");
                }
            }

            private static void Postfix(GameStartState __state)
            {
                if (__state == null)
                    return;

                try
                {
                    var db = PIUDataBase.Instance;
                    var pd = PlayerData.Instance;
                    if (db != null && pd != null && db.songTable != null && db.songTable.ContainsKey(__state.OrigSongId))
                    {
                        pd.Song = db.songTable[__state.OrigSongId];
                        pd.SelectedLevel = __state.OrigLevel;

                        var pack = CustomPackManager.ResolveBySongId(__state.OrigSongId);
                        string loadingImg = pack?.LoadingThumbnailPath ?? pack?.JacketPath;
                        if (!string.IsNullOrEmpty(loadingImg))
                        {
                            var spr = LoadOrGetJacket(loadingImg);
                            if (spr != null)
                                pd.Curtain = spr;
                        }

                        Mod.Log.Msg($"[MainStation.GAMESTART] 복원 완료 → {__state.OrigSongId} ({pd.Song.TitleEng}, Lv.{pd.SelectedLevel})");
                    }
                }
                catch (Exception ex)
                {
                    Mod.Log.Warning($"[MainStation.GAMESTART] 복원 실패: {ex.Message}");
                }
            }
        }

        // 실제 인게임 로딩 커튼 UI(IngameSongInfoUI) 배경 이미지 및 난이도 표시 복원
        [HarmonyPatch(typeof(IngameSongInfoUI), nameof(IngameSongInfoUI.OnShow), typeof(Il2CppReferenceArray<Il2CppSystem.Object>))]
        internal static class IngameSongInfoUI_OnShow_Patch
        {
            private static void Prefix()
            {
                // 관문 Prefix: 예외가 새면 원본 OnShow(로딩 커튼)까지 건너뛰므로 격리
                try
                {
                    if (!ModConfig.CustomEnabled.Value || !Util.IsCurrentSongCustom()) return;
                    var pd = PlayerData.Instance;
                    if (pd != null)
                        pd.SelectedLevel = LastCustomSelectedLevel;
                }
                catch (Exception ex)
                {
                    Mod.Log.Warning($"[IngameSongInfoUI] 난이도 복원 실패(원본 진행): {ex.Message}");
                }
            }

            private static void Postfix(IngameSongInfoUI __instance)
            {
                if (!ModConfig.CustomEnabled.Value || __instance == null || __instance._bgImage == null)
                    return;

                var pd = PlayerData.Instance;
                if (pd == null)
                    return;

                var pack = CustomPackManager.ResolveBySongId(Util.CurrentSongId());
                string loadingImg = pack?.LoadingThumbnailPath ?? pack?.JacketPath;
                if (string.IsNullOrEmpty(loadingImg))
                    return;

                var sprite = LoadOrGetJacket(loadingImg);
                if (sprite != null)
                {
                    __instance._bgImage.sprite = sprite;
                    __instance._bgImage.color = Color.white;
                    pd.Curtain = sprite;
                }

                // 긴 커스텀 곡 제목이 로딩 커튼에서 배지 위로 삐져나오며 스크롤되지 않도록 고정 + 자동 축소
                try
                {
                    var titleTmp = __instance._titleText;
                    if (titleTmp != null)
                    {
                        var behaviours = titleTmp.gameObject.GetComponents<MonoBehaviour>();
                        if (behaviours != null)
                        {
                            for (int i = 0; i < behaviours.Length; i++)
                            {
                                var b = behaviours[i];
                                if (b == null) continue;
                                string tn = b.GetIl2CppType()?.Name ?? "";
                                if (tn.IndexOf("Scroll", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    tn.IndexOf("Marquee", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    tn.IndexOf("Flow", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    tn.IndexOf("Slide", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    b.enabled = false;
                                }
                            }
                        }

                        titleTmp.enableWordWrapping = false;
                        titleTmp.enableAutoSizing = true;
                        titleTmp.fontSizeMin = 24f;
                        titleTmp.fontSizeMax = titleTmp.fontSize > 24f ? titleTmp.fontSize : 60f;
                        titleTmp.overflowMode = Il2CppTMPro.TextOverflowModes.Ellipsis;
                    }
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(IngameSongInfoUI), nameof(IngameSongInfoUI.SetNumberImages))]
        internal static class IngameSongInfoUI_SetNumberImages_Patch
        {
            private static void Prefix(ref int level)
            {
                if (ModConfig.CustomEnabled.Value && Util.IsCurrentSongCustom())
                    level = LastCustomSelectedLevel;
            }
        }

        [HarmonyPatch(typeof(IngameSongInfoUI), nameof(IngameSongInfoUI.SetLevelType))]
        internal static class IngameSongInfoUI_SetLevelType_Patch
        {
            private static void Prefix(ref int lv)
            {
                if (ModConfig.CustomEnabled.Value && Util.IsCurrentSongCustom())
                    lv = LastCustomSelectedLevel;
            }
        }

        // 결과 화면(DANCE GRADE) 상단 바 자켓 이미지 교체
        [HarmonyPatch(typeof(ResultDefaultGameInfo), nameof(ResultDefaultGameInfo.SetData))]
        internal static class ResultDefaultGameInfo_SetData_Patch
        {
            private static void Postfix(ResultDefaultGameInfo __instance, Song song)
            {
                if (!ModConfig.CustomEnabled.Value || __instance == null || __instance._cover == null)
                    return;

                var pack = CustomPackManager.ResolveBySongId(song?.ID);
                if (pack == null || string.IsNullOrEmpty(pack.JacketPath))
                    return;

                var sprite = LoadOrGetJacket(pack.JacketPath);
                if (sprite != null)
                {
                    __instance._cover.sprite = sprite;
                    __instance._cover.color = Color.white;
                }
            }
        }

        [HarmonyPatch(typeof(IngameSongInfoUI), nameof(IngameSongInfoUI.SetInfoData))]
        internal static class IngameSongInfoUI_SetInfoData_Patch
        {
            private static void Postfix(IngameSongInfoUI __instance, Song currentSong)
            {
                if (!ModConfig.CustomEnabled.Value || __instance == null || __instance._bgImage == null)
                    return;

                var pack = CustomPackManager.ResolveBySongId(currentSong?.ID);
                string loadingImg = pack?.LoadingThumbnailPath ?? pack?.JacketPath;
                if (string.IsNullOrEmpty(loadingImg))
                    return;

                var sprite = LoadOrGetJacket(loadingImg);
                if (sprite != null)
                {
                    __instance._bgImage.sprite = sprite;
                    __instance._bgImage.color = Color.white;
                }
            }
        }

        // 멀티 방 생성 창(F3): 원본 SetSongInit이 NullReferenceException으로 죽으면 호출한 MultiStageRoomListUI.DoCreateRoom이 중단되어
        // 창의 SetKeyAction(키 등록)이 실행되지 않고 ESC/ENTER가 먹통이 된다(2026-11-23 실기).
        // 진짜 원인은 오프라인에서 PlayerData.DLCEntrys가 null인 것(공식 곡으로도 재현) → Prefix 첫 줄의 EnsureOfflineDlcEntries가 해결한다.
        // 커스텀 곡 ID는 자켓 스프라이트 조회가 비는 등 원본에 넘기기 안전하지 않아, 다른 UI와 같은 방식으로 원본에는 공식 템플릿 곡 ID를 넘기고
        // Postfix에서 제목·자켓만 커스텀으로 덮어쓴다.
        [HarmonyPatch(typeof(MultiStageCreateRoomPopup), nameof(MultiStageCreateRoomPopup.SetSongInit))]
        internal static class MultiStageCreateRoomPopup_SetSongInit_Patch
        {
            private static void Prefix(ref string songId, out string __state)
            {
                __state = null;
                OfflineStubPatches.EnsureOfflineDlcEntries("CreateRoomPopup.SetSongInit"); // 진짜 원인: 오프라인에서 DLCEntrys가 null → 원본이 NRE
                try
                {
                    if (!ModConfig.CustomEnabled.Value || string.IsNullOrEmpty(TemplateOfficialSongId))
                        return;
                    if (CustomPackManager.ResolveBySongId(songId) == null)
                        return;

                    __state = songId;            // Postfix에서 커스텀 제목·자켓을 되돌려 놓기 위해 원래 ID를 기억
                    songId = TemplateOfficialSongId;
                }
                catch (Exception ex)
                {
                    Mod.Log.Warning($"[CreateRoomPopup] 템플릿 곡 치환 실패: {ex.Message}");
                }
            }

            private static void Postfix(MultiStageCreateRoomPopup __instance, string __state)
            {
                if (__state == null || __instance == null)
                    return;

                try
                {
                    var pack = CustomPackManager.ResolveBySongId(__state);
                    if (pack == null)
                        return;

                    if (__instance._songName != null && !string.IsNullOrEmpty(pack.Title))
                        __instance._songName.text = pack.Title;

                    if (__instance._eyeCatch != null && !string.IsNullOrEmpty(pack.JacketPath))
                    {
                        var sprite = LoadOrGetJacket(pack.JacketPath);
                        if (sprite != null)
                        {
                            __instance._eyeCatch.sprite = sprite;
                            __instance._eyeCatch.color = Color.white;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Mod.Log.Warning($"[CreateRoomPopup] 커스텀 제목·자켓 복원 실패: {ex.Message}");
                }
            }
        }

        // 곡 로딩 화면(BGAPlayerLoader) 배경 이미지도 커스텀 로딩 썸네일(16:9 우선)로 교체
        [HarmonyPatch(typeof(BGAPlayerLoader), nameof(BGAPlayerLoader.SetInfoData))]
        internal static class BGAPlayerLoader_SetInfoData_Patch
        {
            private static void Postfix(BGAPlayerLoader __instance, Song currentSong)
            {
                if (!ModConfig.CustomEnabled.Value || __instance == null || __instance._bgImage == null)
                    return;

                var pack = CustomPackManager.ResolveBySongId(currentSong?.ID);
                string loadingImg = pack?.LoadingThumbnailPath ?? pack?.JacketPath;
                if (string.IsNullOrEmpty(loadingImg))
                    return;

                var sprite = LoadOrGetJacket(loadingImg);
                if (sprite != null)
                {
                    __instance._bgImage.sprite = sprite;
                    __instance._bgImage.color = Color.white;
                }
            }
        }

        [HarmonyPatch(typeof(SongListContent), nameof(SongListContent.GetEyecatchSprite))]
        internal static class SongListContent_GetEyecatchSprite_Patch
        {
            private static void Postfix(SongListContent __instance, ref Sprite __result)
            {
                if (!ModConfig.CustomEnabled.Value || __instance == null)
                    return;

                var pack = CustomPackManager.ResolveBySongId(Util.SongId(__instance.Song));
                if (pack == null || string.IsNullOrEmpty(pack.JacketPath))
                    return;

                var sprite = LoadOrGetJacket(pack.JacketPath);
                if (sprite != null)
                    __result = sprite;
            }
        }

        [HarmonyPatch(typeof(SongListContent), nameof(SongListContent.GetArcadeEyecatchSprite))]
        internal static class SongListContent_GetArcadeEyecatchSprite_Patch
        {
            private static void Postfix(SongListContent __instance, ref Sprite __result)
            {
                if (!ModConfig.CustomEnabled.Value || __instance == null)
                    return;

                var pack = CustomPackManager.ResolveBySongId(Util.SongId(__instance.Song));
                if (pack == null || string.IsNullOrEmpty(pack.JacketPath))
                    return;

                var sprite = LoadOrGetJacket(pack.JacketPath);
                if (sprite != null)
                    __result = sprite;
            }
        }
    }
}
