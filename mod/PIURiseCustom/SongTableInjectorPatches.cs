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
        private static bool _templateFromMainTable;

        // 마지막으로 선택한 커스텀 곡의 원래 난이도 (로딩 화면/인게임 UI 표시 복원용)
        internal static int LastCustomSelectedLevel = 10;

        private static readonly Dictionary<string, Sprite> _jacketCache = new(StringComparer.OrdinalIgnoreCase);
        // 디코딩에 실패한 이미지 경로. 선곡 리스트 스크롤마다(UpdateDisplayContent) 깨진 파일을 다시 읽어 프레임이 끊기는 것을 막는다.
        private static readonly HashSet<string> _jacketFailed = new(StringComparer.OrdinalIgnoreCase);

        private static void InjectIntoTable(Il2CppSystem.Collections.Generic.Dictionary<string, Song> table, string tableName, bool isMainTable)
        {
            if (!ModConfig.CustomEnabled.Value || table == null)
                return;

            var songs = CustomPackManager.CatalogSongs;
            if (songs.Count == 0)
                return;

            // 공식 곡 중 하나를 찾아 Channel/Generation 기본값 및 템플릿 ID 확보.
            // 템플릿 ID는 GAMESTART·LoadStep이 쓰므로 songTable 곡이 우선이다(v0.3.11). 아케이드 표가 먼저 캐시되면 거기서 임시로 정하되,
            // 뒤이어 songTable을 주입할 때 songTable 곡으로 다시 정한다(아케이드 전용 곡이 템플릿으로 남지 않게).
            bool mayPickTemplateId = TemplateOfficialSongId == null || (isMainTable && !_templateFromMainTable);
            Song template = null;
            foreach (var kv in table)
            {
                if (!CustomPackManager.IsCatalogSongId(kv.Key))
                {
                    template = kv.Value;
                    if (mayPickTemplateId)
                    {
                        TemplateOfficialSongId = kv.Key;
                        _templateFromMainTable = isMainTable;
                        TemplateOfficialLevel = 1; // 다시 정할 때 이전 템플릿의 난이도가 남지 않게
                        TemplateOfficialPlayType = "Single";
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
                        InjectIntoTable(db.songTable, "songTable", isMainTable: true);
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
                        InjectIntoTable(db.acSongTable, "acSongTable", isMainTable: false);
                }
                catch (Exception ex)
                {
                    Mod.Log.Error($"[SongInjector] acSongTable 주입 실패(공식 곡은 정상 유지): {ex}");
                }
            }
        }

        // ===== 자켓·로딩 썸네일 백그라운드 디코딩 (v0.3.12) =====
        // ImageConversion.LoadImage는 메인 스레드에서 PNG를 동기로 푼다(실곡 1024x1024 자켓 약 30ms, 1888x1056 로딩 썸네일 약 60ms —
        // 같은 파일을 관리 디코더로 푼 시간). 선곡 리스트가 처음 그려질 때·곡 시작 때 그만큼 프레임이 멈췄다.
        // PNG는 PngDecoder로 스레드 풀에서 RGBA까지 풀어 두고, 메인 스레드는 LoadRawTextureData(복사)+Apply만 한다.
        // 디코딩 결과는 한 번 쓰면 버린다(이후는 _jacketCache의 Sprite). PNG가 아니거나 디코더가 거부하면 예전 LoadImage 경로.
        // 모든 진입점(Prefetch*/LoadOrGetJacket)은 메인 스레드에서만 불린다 → _decodePending에 잠금이 필요 없다.
        private const long JacketPrefetchBudgetBytes = 96L * 1024 * 1024; // 아직 안 쓴 디코딩 결과(RGBA)의 합 상한
        private static readonly Dictionary<string, (System.Threading.Tasks.Task<DecodedImage> Task, long Bytes)> _decodePending =
            new(StringComparer.OrdinalIgnoreCase);
        private static long _decodePendingBytes;
        private static string _hoverPrefetchPath; // 커서 진입으로 미리 푼 로딩 썸네일(곡을 안 고르고 넘어가면 버린다)

        /// <summary>부팅 때: 목록에 나올 커스텀 곡 자켓을 백그라운드에서 미리 푼다(메인 스레드 전용 호출).</summary>
        internal static void PrefetchCatalogJackets()
        {
            if (!ModConfig.CustomEnabled.Value || !ModConfig.BackgroundImageDecode.Value)
                return;
            int started = 0;
            foreach (var p in CustomPackManager.CatalogSongs)
                if (PrefetchImage(p.JacketPath)) started++;
            if (started > 0)
                Mod.Log.Msg($"[CustomJacket] 자켓 {started}개 백그라운드 디코딩 시작 (대기 중 RGBA {_decodePendingBytes / 1024} KB, 상한 {JacketPrefetchBudgetBytes / (1024 * 1024)} MB)");
        }

        /// <summary>선곡 커서가 곡에 올라갔을 때: 곡 시작 때 쓸 로딩 썸네일을 미리 푼다. 직전 곡의 안 쓴 결과는 버린다.</summary>
        internal static void PrefetchHoverImage(string path)
        {
            if (!ModConfig.BackgroundImageDecode.Value || string.IsNullOrEmpty(path))
                return;
            if (_hoverPrefetchPath != null && !string.Equals(_hoverPrefetchPath, path, StringComparison.OrdinalIgnoreCase))
                TakePending(_hoverPrefetchPath); // 버린다(작업이 끝나면 결과는 GC가 거둔다)
            if (PrefetchImage(path))
                _hoverPrefetchPath = path;
        }

        private static bool PrefetchImage(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    return false;
                if (_decodePending.ContainsKey(path) || _jacketFailed.Contains(path) || _jacketCache.ContainsKey(path))
                    return false;
                if (!CustomPackManager.TryReadImageDimensions(path, out int w, out int h) || w <= 0 || h <= 0)
                    return false;
                long bytes = (long)w * h * 4;
                if (_decodePendingBytes + bytes > JacketPrefetchBudgetBytes)
                    return false; // 상한을 넘는 것은 쓸 때 예전 방식으로 푼다

                var task = System.Threading.Tasks.Task.Run(() =>
                {
                    try { return PngDecoder.TryDecode(File.ReadAllBytes(path)); }
                    catch { return null; }
                });
                _decodePending[path] = (task, bytes);
                _decodePendingBytes += bytes;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static System.Threading.Tasks.Task<DecodedImage> TakePending(string path)
        {
            if (!_decodePending.TryGetValue(path, out var e))
                return null;
            _decodePending.Remove(path);
            _decodePendingBytes -= e.Bytes;
            if (string.Equals(_hoverPrefetchPath, path, StringComparison.OrdinalIgnoreCase))
                _hoverPrefetchPath = null;
            return e.Task;
        }

        private static Texture2D TextureFromDecoded(DecodedImage img)
        {
            var tex = new Texture2D(img.Width, img.Height, TextureFormat.RGBA32, false);
            var handle = System.Runtime.InteropServices.GCHandle.Alloc(img.Rgba, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                // Unity가 즉시 자기 버퍼로 복사하므로 고정은 이 호출 동안만 필요하다(Il2Cpp 배열을 따로 만들지 않는다)
                tex.LoadRawTextureData(handle.AddrOfPinnedObject(), img.Rgba.Length);
            }
            catch
            {
                UnityEngine.Object.Destroy(tex);
                throw;
            }
            finally
            {
                handle.Free();
            }
            tex.Apply(false, false); // LoadImage(…, markNonReadable: false)와 같게 읽기 가능 상태 유지
            return tex;
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

            var sw = System.Diagnostics.Stopwatch.StartNew();
            Texture2D tex = null;
            string how = null;

            // 1) 백그라운드에서 미리 푼 결과가 있으면 원시 픽셀만 올린다
            var pending = TakePending(jacketPath);
            if (pending != null)
            {
                try
                {
                    bool wasDone = pending.IsCompleted;
                    var img = pending.GetAwaiter().GetResult();
                    if (img != null)
                    {
                        double waitMs = sw.Elapsed.TotalMilliseconds;
                        tex = TextureFromDecoded(img);
                        double uploadMs = sw.Elapsed.TotalMilliseconds - waitMs;
                        how = wasDone ? $"미리 디코딩됨, 업로드 {uploadMs:0.0}ms"
                                      : $"미리 디코딩 대기 {waitMs:0.0}ms + 업로드 {uploadMs:0.0}ms";
                    }
                }
                catch (Exception ex)
                {
                    tex = null;
                    Mod.Log.Warning($"[CustomJacket] 미리 디코딩 결과 사용 실패 → 기존 방식으로 다시 읽음 ({Path.GetFileName(jacketPath)}): {ex.Message}");
                }
            }

            try
            {
                // 2) 예전 방식: 메인 스레드에서 Unity가 직접 디코딩 (JPEG, 미리 풀지 않은 파일, 디코더가 거부한 PNG)
                if (tex is null)
                {
                    sw.Restart();
                    byte[] bytes = File.ReadAllBytes(jacketPath);
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    bool ok = ImageConversion.LoadImage(tex, new Il2CppStructArray<byte>(bytes), false);
                    if (!ok)
                    {
                        UnityEngine.Object.Destroy(tex);
                        _jacketFailed.Add(jacketPath);
                        Mod.Log.Warning($"[CustomJacket] ImageConversion.LoadImage 실패 ({jacketPath}, {bytes.Length} bytes) — 재시작 전까지 다시 시도하지 않음");
                        return null;
                    }
                    how = $"메인 스레드 디코딩 {sw.Elapsed.TotalMilliseconds:0.0}ms";
                }

                var sprite = Sprite.Create(
                    tex,
                    new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    100f);

                sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                _jacketCache[jacketPath] = sprite;
                Mod.Log.Msg($"[CustomJacket] 자켓 로드 성공: {Path.GetFileName(jacketPath)} ({tex.width}x{tex.height}) — {how}");
                return sprite;
            }
            catch (Exception ex)
            {
                _jacketFailed.Add(jacketPath);
                Mod.Log.Warning($"[CustomJacket] 로드 실패 ({jacketPath}): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Image.sprite에 새 스프라이트를 대입하되, 이미 동일한 스프라이트가 적용되어 있으면
        /// 네이티브 프로퍼티 세터 호출과 내부 UI 리빌드를 건너뛴다(v0.3.17).
        /// </summary>
        internal static void SetSpriteIfChanged(UnityEngine.UI.Image img, Sprite sprite)
        {
            if (img is null || sprite is null) return;
            var cur = img.sprite;
            if (cur is null || cur.Pointer != sprite.Pointer)
            {
                img.sprite = sprite;
                img.color = Color.white;
            }
        }

        [HarmonyPatch(typeof(SongDataCell), nameof(SongDataCell.Init), typeof(string), typeof(byte), typeof(int))]
        internal static class SongDataCell_Init_Patch
        {
            private static void Postfix(SongDataCell __instance, string songID)
            {
                try
                {
                    if (!ModConfig.CustomEnabled.Value || __instance is null)
                        return;
                    var pack = CustomPackManager.ResolveBySongId(songID);
                    if (pack == null || string.IsNullOrEmpty(pack.JacketPath) || __instance._eyeCatch == null)
                        return;

                    var sprite = LoadOrGetJacket(pack.JacketPath);
                    SetSpriteIfChanged(__instance._eyeCatch, sprite);
                }
                catch { }
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
            try
            {
                // 리스트 스크롤마다 박스 수 × 2번 불린다. 공식 곡은 관리 사전 조회로 먼저 거르고,
                // Unity 객체 비교(_eyecatchImage == null, IL2CPP 호출)는 커스텀 곡일 때만 한다(v0.3.14).
                if (!ModConfig.CustomEnabled.Value || box is null)
                    return;

                var content = box.Content;
                if (content is null)
                    return;

                var pack = CustomPackManager.ResolveBySongId(Util.SongId(content.Song));
                if (pack == null || string.IsNullOrEmpty(pack.JacketPath))
                    return;

                var img = box._eyecatchImage;
                if (img is null || img == null)
                    return;

                var sprite = LoadOrGetJacket(pack.JacketPath);
                SetSpriteIfChanged(img, sprite);
            }
            catch { }
        }

        // 커스텀 곡은 잠금 검사 무조건 통과
        [HarmonyPatch(typeof(MainStation), nameof(MainStation.CheckSongLock))]
        internal static class MainStation_CheckSongLock_Patch
        {
            private static bool Prefix(string songID, ref bool __result)
            {
                try
                {
                    if (ModConfig.CustomEnabled.Value && CustomPackManager.IsCatalogSongId(songID))
                    {
                        __result = false;
                        return false;
                    }
                }
                catch { }
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
                try
                {
                    if (!ModConfig.CustomEnabled.Value)
                        return;
                    var pack = CustomPackManager.ResolveBySongId(Util.CurrentSongId());
                    string loadingImg = pack?.LoadingThumbnailPath ?? pack?.JacketPath;
                    if (string.IsNullOrEmpty(loadingImg) || __instance == null || __instance._bgImage == null)
                        return;

                    var pd = PlayerData.Instance;
                    if (pd == null)
                        return;

                    var sprite = LoadOrGetJacket(loadingImg);
                    if (sprite != null)
                    {
                        SetSpriteIfChanged(__instance._bgImage, sprite);
                        pd.Curtain = sprite;
                    }
                }
                catch (Exception ex)
                {
                    Mod.Log.Warning($"[IngameSongInfoUI] 로딩 커튼 이미지 교체 실패(원본 이미지 유지): {ex.Message}");
                    return;
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
                try
                {
                    if (ModConfig.CustomEnabled.Value && Util.IsCurrentSongCustom())
                        level = LastCustomSelectedLevel;
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(IngameSongInfoUI), nameof(IngameSongInfoUI.SetLevelType))]
        internal static class IngameSongInfoUI_SetLevelType_Patch
        {
            private static void Prefix(ref int lv)
            {
                try
                {
                    if (ModConfig.CustomEnabled.Value && Util.IsCurrentSongCustom())
                        lv = LastCustomSelectedLevel;
                }
                catch { }
            }
        }

        // 결과 화면(DANCE GRADE) 상단 바 자켓 이미지 교체
        [HarmonyPatch(typeof(ResultDefaultGameInfo), nameof(ResultDefaultGameInfo.SetData))]
        internal static class ResultDefaultGameInfo_SetData_Patch
        {
            private static void Postfix(ResultDefaultGameInfo __instance, Song song)
            {
                try
                {
                    if (!ModConfig.CustomEnabled.Value || __instance == null || __instance._cover == null)
                        return;

                    var pack = CustomPackManager.ResolveBySongId(Util.SongId(song));
                    if (pack == null || string.IsNullOrEmpty(pack.JacketPath))
                        return;

                    var sprite = LoadOrGetJacket(pack.JacketPath);
                    SetSpriteIfChanged(__instance._cover, sprite);
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(IngameSongInfoUI), nameof(IngameSongInfoUI.SetInfoData))]
        internal static class IngameSongInfoUI_SetInfoData_Patch
        {
            private static void Postfix(IngameSongInfoUI __instance, Song currentSong)
            {
                try
                {
                    if (!ModConfig.CustomEnabled.Value || __instance == null || __instance._bgImage == null)
                        return;

                    var pack = CustomPackManager.ResolveBySongId(Util.SongId(currentSong));
                    string loadingImg = pack?.LoadingThumbnailPath ?? pack?.JacketPath;
                    if (string.IsNullOrEmpty(loadingImg))
                        return;

                    var sprite = LoadOrGetJacket(loadingImg);
                    SetSpriteIfChanged(__instance._bgImage, sprite);
                }
                catch { }
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
                        SetSpriteIfChanged(__instance._eyeCatch, sprite);
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
                try
                {
                    if (!ModConfig.CustomEnabled.Value)
                        return;
                    var pack = CustomPackManager.ResolveBySongId(Util.SongId(currentSong));
                    string loadingImg = pack?.LoadingThumbnailPath ?? pack?.JacketPath;
                    if (string.IsNullOrEmpty(loadingImg) || __instance == null || __instance._bgImage == null)
                        return;

                    var sprite = LoadOrGetJacket(loadingImg);
                    SetSpriteIfChanged(__instance._bgImage, sprite);
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(SongListContent), nameof(SongListContent.GetEyecatchSprite))]
        internal static class SongListContent_GetEyecatchSprite_Patch
        {
            // 반환값이 있는 메서드의 Postfix가 예외를 던지면 IL2CPP 트램펄린이 기본값(null)을 돌려줘 공식 곡 자켓까지 사라진다 → 격리
            private static void Postfix(SongListContent __instance, ref Sprite __result)
            {
                try
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
                catch { }
            }
        }

        [HarmonyPatch(typeof(SongListContent), nameof(SongListContent.GetArcadeEyecatchSprite))]
        internal static class SongListContent_GetArcadeEyecatchSprite_Patch
        {
            // 반환값이 있는 메서드의 Postfix가 예외를 던지면 IL2CPP 트램펄린이 기본값(null)을 돌려줘 공식 곡 자켓까지 사라진다 → 격리
            private static void Postfix(SongListContent __instance, ref Sprite __result)
            {
                try
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
                catch { }
            }
        }
    }
}
