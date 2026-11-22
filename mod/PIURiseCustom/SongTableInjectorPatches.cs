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
    /// hwa/ 하위 폴더의 커스텀 곡들을 PIUDataBase.songTable에 독립 곡(HWA_0001...)으로 등록하고,
    /// 선곡 화면 자켓(EyecatchSprite)을 폴더의 cover.png / Thumbnail.png 등으로 교체한다.
    /// </summary>
    internal static class SongTableInjectorPatches
    {
        // PlayBase.LoadStep에서 HWA_ 커스텀 곡을 로드할 때 원본 DataAssets 경로 통과용으로 쓰는 공식 템플릿 곡 정보
        internal static string TemplateOfficialSongId;
        internal static int TemplateOfficialLevel = 1;
        internal static string TemplateOfficialPlayType = "Single";

        // 마지막으로 선택한 커스텀 곡의 원래 난이도 (로딩 화면/인게임 UI 표시 복원용)
        internal static int LastCustomSelectedLevel = 10;

        private static readonly Dictionary<string, Sprite> _jacketCache = new(StringComparer.OrdinalIgnoreCase);

        public static void ClearJacketCache()
        {
            _jacketCache.Clear();
        }

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
                var db = PIUDataBase.Instance;
                if (db != null)
                    InjectIntoTable(db.songTable, "songTable");
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
                var db = PIUDataBase.Instance;
                if (db != null)
                    InjectIntoTable(db.acSongTable, "acSongTable");
            }
        }

        internal static Sprite LoadOrGetJacket(string jacketPath)
        {
            if (string.IsNullOrEmpty(jacketPath) || !File.Exists(jacketPath))
                return null;

            if (_jacketCache.TryGetValue(jacketPath, out var cached) && cached != null)
                return cached;

            try
            {
                byte[] bytes = File.ReadAllBytes(jacketPath);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                bool ok = ImageConversion.LoadImage(tex, new Il2CppStructArray<byte>(bytes), false);
                if (!ok)
                {
                    Mod.Log.Warning($"[CustomJacket] ImageConversion.LoadImage 실패 ({jacketPath}, {bytes.Length} bytes)");
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

            var pack = CustomPackManager.ResolveBySongId(content.Song.ID);
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
                if (!ModConfig.CustomEnabled.Value) return;
                var pd = PlayerData.Instance;
                if (pd != null && CustomPackManager.IsCatalogSongId(pd.Song.ID))
                    pd.SelectedLevel = LastCustomSelectedLevel;
            }

            private static void Postfix(IngameSongInfoUI __instance)
            {
                if (!ModConfig.CustomEnabled.Value || __instance == null || __instance._bgImage == null)
                    return;

                var pd = PlayerData.Instance;
                if (pd == null)
                    return;

                var pack = CustomPackManager.ResolveBySongId(pd.Song.ID);
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
                if (!ModConfig.CustomEnabled.Value) return;
                var pd = PlayerData.Instance;
                if (pd != null && CustomPackManager.IsCatalogSongId(pd.Song.ID))
                    level = LastCustomSelectedLevel;
            }
        }

        [HarmonyPatch(typeof(IngameSongInfoUI), nameof(IngameSongInfoUI.SetLevelType))]
        internal static class IngameSongInfoUI_SetLevelType_Patch
        {
            private static void Prefix(ref int lv)
            {
                if (!ModConfig.CustomEnabled.Value) return;
                var pd = PlayerData.Instance;
                if (pd != null && CustomPackManager.IsCatalogSongId(pd.Song.ID))
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

                string songId = __instance.Song.ID;
                var pack = CustomPackManager.ResolveBySongId(songId);
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

                string songId = __instance.Song.ID;
                var pack = CustomPackManager.ResolveBySongId(songId);
                if (pack == null || string.IsNullOrEmpty(pack.JacketPath))
                    return;

                var sprite = LoadOrGetJacket(pack.JacketPath);
                if (sprite != null)
                    __result = sprite;
            }
        }
    }
}
