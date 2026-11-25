using System;
using System.IO;
using System.Threading.Tasks;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPIUMobileStepDLL;
using UnityEngine.Video;

namespace PIURiseCustom
{
    internal static class CustomMediaPatches
    {
        // PlayBase.LoadStep에서 커스텀 곡(990001…)을 템플릿 ID로 우회 통과시킬 때 실제 팩을 Step.Load Postfix로 전달하는 슬롯
        private static CustomPack _pendingCustomPack;

        // ===== VideoPlayer 이벤트 핸들러 정리 =====
        // add_prepareCompleted/add_errorReceived로 붙인 핸들러는 떼지 않으면 같은 VideoPlayer에 계속 쌓인다.
        // 선곡 화면에서 커스텀 곡 사이를 오갈 때마다 하나씩 늘어나 Prepare 완료 때마다 지난 핸들러가 전부 호출됐다(v0.3.4에서 직전 것을 뗌).
        private sealed class VideoHandlers
        {
            public VideoPlayer Player;
            public VideoPlayer.EventHandler Prepared;
            public VideoPlayer.ErrorEventHandler Error;

            public void Detach()
            {
                var vp = Player;
                if (vp is not null)
                {
                    if (Prepared is not null) { try { vp.remove_prepareCompleted(Prepared); } catch { } }
                    if (Error is not null) { try { vp.remove_errorReceived(Error); } catch { } }
                }
                Player = null;
                Prepared = null;
                Error = null;
            }
        }
        private static readonly VideoHandlers _previewHandlers = new();
        private static readonly VideoHandlers _bgaHandlers = new();

        private static string GetCurrentSongId() => Util.CurrentSongId();

        // ===== 오디오 캐시 (최근 2개) =====
        // 선곡 화면에서 곡을 오갈 때마다, 그리고 프리뷰 → 본게임 진입 때 같은 music.ogg를 메인 스레드에서 다시 통째로 읽어
        // 커서 이동이 끊기던 문제를 줄인다. 파일 크기·수정 시각이 바뀌면 다시 읽는다.
        // v0.3.10: (1) 커서가 곡에 올라가면 파일을 백그라운드 스레드에서 미리 읽는다(PrefetchAudio).
        //          (2) 게임에 넘기는 Il2Cpp byte[]까지 캐시해, 같은 곡을 다시 불러도 수 MB 배열 할당·복사를 하지 않는다.
        //              FMOD는 OPENMEMORY로 받은 데이터를 자기 버퍼로 복사하므로 같은 배열을 다시 넘겨도 안전하다.
        //          Il2Cpp 배열은 메인 스레드에서만 만든다. 만든 뒤에는 관리 쪽 byte[]는 놓아 메모리를 두 번 들고 있지 않는다.
        private const long AudioCacheMaxBytes = 64L * 1024 * 1024;
        private sealed class AudioCacheEntry
        {
            public string Path;
            public long Length;
            public DateTime WriteUtc;
            public Task<byte[]> Read;                // 미리 읽기 작업 (Il2Cpp 배열을 만든 뒤에는 null)
            public Il2CppStructArray<byte> IlBytes;  // 메인 스레드에서 만든 게임 전달용 배열
        }
        private static readonly AudioCacheEntry[] _audioCache = new AudioCacheEntry[2];

        private static AudioCacheEntry FindAudioEntry(string path, long len, DateTime writeUtc)
        {
            for (int i = 0; i < _audioCache.Length; i++)
            {
                var e = _audioCache[i];
                if (e != null && e.Length == len && e.WriteUtc == writeUtc &&
                    string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase))
                {
                    if (i != 0) { _audioCache[i] = _audioCache[0]; _audioCache[0] = e; } // MRU 앞으로
                    return e;
                }
            }
            return null;
        }

        private static AudioCacheEntry AddAudioEntry(string path, long len, DateTime writeUtc)
        {
            var e = new AudioCacheEntry { Path = path, Length = len, WriteUtc = writeUtc };
            _audioCache[1] = _audioCache[0];
            _audioCache[0] = e;
            return e;
        }

        /// <summary>선곡 커서가 곡에 올라갔을 때: 오디오 파일을 백그라운드에서 읽어 둔다(메인 스레드에서 호출).</summary>
        internal static void PrefetchAudio(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length > AudioCacheMaxBytes)
                    return;
                if (FindAudioEntry(path, fi.Length, fi.LastWriteTimeUtc) != null)
                    return;
                var e = AddAudioEntry(path, fi.Length, fi.LastWriteTimeUtc);
                e.Read = Task.Run(() => File.ReadAllBytes(path));
            }
            catch { }
        }

        /// <summary>게임에 넘길 Il2Cpp 오디오 배열을 돌려준다(메인 스레드 전용). source는 성능 로그용 설명.</summary>
        private static Il2CppStructArray<byte> GetAudioIl2Cpp(string path, out string source)
        {
            var fi = new FileInfo(path);
            long len = fi.Length;
            DateTime writeUtc = fi.LastWriteTimeUtc;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var e = FindAudioEntry(path, len, writeUtc);
            if (e?.IlBytes != null)
            {
                source = "Il2Cpp 배열 캐시";
                return e.IlBytes;
            }

            byte[] bytes = null;
            string how = "디스크에서 바로 읽음";
            if (e?.Read != null)
            {
                bool done = e.Read.IsCompleted;
                try
                {
                    bytes = e.Read.GetAwaiter().GetResult();
                    how = done ? "미리 읽어 둔 바이트" : "미리 읽기 완료 대기";
                }
                catch { bytes = null; } // 미리 읽기가 실패했으면 아래에서 직접 읽는다
            }
            if (bytes == null)
                bytes = File.ReadAllBytes(path);
            double readMs = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var il = new Il2CppStructArray<byte>(bytes);
            double copyMs = sw.Elapsed.TotalMilliseconds;

            if (bytes.LongLength <= AudioCacheMaxBytes)
            {
                e ??= AddAudioEntry(path, len, writeUtc);
                e.IlBytes = il;
                e.Read = null; // 관리 쪽 사본은 놓는다
            }

            source = $"{how} {readMs:0.0}ms + Il2Cpp 복사 {copyMs:0.0}ms ({bytes.LongLength / 1024} KB)";
            return il;
        }

        // ===== BGA 파일 URL 캐시 =====
        private static readonly System.Collections.Generic.Dictionary<string, string> _fileUrlCache = new(StringComparer.OrdinalIgnoreCase);

        private static string FileUrl(string path)
        {
            if (!_fileUrlCache.TryGetValue(path, out string url))
            {
                url = new Uri(Path.GetFullPath(path)).AbsoluteUri;
                _fileUrlCache[path] = url;
            }
            return url;
        }

        /// <summary>
        /// 선곡 커서가 커스텀 곡에 올라갔을 때 부른다: 오디오 파일과 차트를 백그라운드에서 미리 읽어 둬
        /// 미리듣기·본게임 로딩 때 메인 스레드가 디스크 읽기·파싱을 기다리지 않게 한다.
        /// </summary>
        internal static void PrefetchPack(CustomPack pack)
        {
            if (pack == null)
                return;
            if (ModConfig.OverridePreviewAudio.Value)
                PrefetchAudio(pack.PreviewBgmPath ?? pack.BgmPath);
            if (!string.IsNullOrEmpty(pack.ChartPath))
                CustomChartParsers.Prefetch(pack.ChartPath, ModConfig.CustomChartOffsetMs.Value);
            // 곡을 고르면 로딩 커튼에 쓸 16:9 썸네일(없으면 자켓)도 미리 풀어 둔다(v0.3.12)
            SongTableInjectorPatches.PrefetchHoverImage(pack.LoadingThumbnailPath ?? pack.JacketPath);
        }

        private static string GetCurrentPlayType()
        {
            try
            {
                var pd = PlayerData.Instance;
                if (pd == null) return "Single";
                int t = (int)pd.EInputType;
                return t == 0 ? "Single" : (t == 1 ? "HalfDouble" : "Double");
            }
            catch { return "Single"; }
        }

        private static int GetCurrentLevel()
        {
            try
            {
                var pd = PlayerData.Instance;
                return pd != null ? pd.SelectedLevel : 0;
            }
            catch { return 0; }
        }

        // 0. PlayBase.LoadStep(songId, lv, playType): 커스텀 독립 곡의 팩을 Step.Load(byte[])에 전달한다.
        [HarmonyPatch(typeof(PlayBase), nameof(PlayBase.LoadStep))]
        internal static class PlayBase_LoadStep_Patch
        {
            private static void Prefix(ref string songId, ref int lv, ref string playType)
            {
                _pendingCustomPack = null;
                if (!ModConfig.CustomEnabled.Value)
                    return;

                try
                {
                    string effectiveSongId = string.IsNullOrEmpty(songId) ? GetCurrentSongId() : songId;
                    var pack = CustomPackManager.Resolve(effectiveSongId, playType, lv);
                    if (pack == null)
                        return;

                    _pendingCustomPack = pack;

                    // hwa 독립 곡은 DataAssets 해시 파일이 없으므로 공식 템플릿 곡으로 파일 읽기 관문만 통과
                    if (CustomPackManager.IsCatalogSongId(effectiveSongId) &&
                        !string.IsNullOrEmpty(SongTableInjectorPatches.TemplateOfficialSongId))
                    {
                        songId = SongTableInjectorPatches.TemplateOfficialSongId;
                        lv = SongTableInjectorPatches.TemplateOfficialLevel;
                        playType = SongTableInjectorPatches.TemplateOfficialPlayType;
                        Mod.Log.Msg($"[CustomChart] 독립 곡 {effectiveSongId} → 템플릿({songId} {playType} Lv.{lv}) 경유 로드");
                    }
                }
                catch (Exception ex)
                {
                    _pendingCustomPack = null;
                    Mod.Log.Warning($"[CustomChart] LoadStep 전처리 오류(원본 경로로 진행): {ex.Message}");
                }
            }
        }

        // 1. 커스텀 차트 주입: PIUMobileStepDLL.Step.Load(byte[]) Postfix
        //    원본 템플릿의 Step.Load(byte[])를 먼저 통과시켜 pParam(스킨/LED/UI 파라미터)을 그대로 보존한 뒤,
        //    nNote / nBlock / pBlock만 커스텀 차트(또는 뼈대 빈 차트)로 교체하고 InitData()를 다시 호출한다.
        [HarmonyPatch(typeof(Step), nameof(Step.Load), typeof(Il2CppStructArray<byte>))]
        internal static class Step_LoadBytes_Patch
        {
            private static void Postfix(Step __instance, ref bool __result)
            {
                if (!ModConfig.CustomEnabled.Value || !__result || __instance == null || __instance.mStep == null)
                    return;

                var pack = _pendingCustomPack ?? CustomPackManager.Resolve(GetCurrentSongId(), GetCurrentPlayType(), GetCurrentLevel());
                // 한 번 쓴 슬롯은 비운다. 남겨 두면 LoadStep을 거치지 않는 다음 Step.Load(다른 모드·화면)에 지난 커스텀 차트가 들어갈 수 있다.
                _pendingCustomPack = null;
                if (pack == null)
                    return;

                try
                {
                    Step_t parsed;
                    if (string.IsNullOrEmpty(pack.ChartPath))
                    {
                        int lanes = GetCurrentPlayType().Equals("Single", StringComparison.OrdinalIgnoreCase) ? 5 : 10;
                        float bpm = float.TryParse(pack.BpmText, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out float b) && b > 0 ? b : 120f;
                        Mod.Log.Msg($"[CustomChart] '{pack.Title}' 차트 없음(뼈대 테스트 모드) → 노트 0개 빈 차트 주입 ({lanes}키, BPM {bpm})");
                        parsed = CustomChartParsers.CreateEmptyStep(lanes, bpm, 300f);
                    }
                    else
                    {
                        Mod.Log.Msg($"[CustomChart] '{pack.Title}' 차트 주입 시작: {Path.GetFileName(pack.ChartPath)}");
                        parsed = CustomChartParsers.LoadFromFile(pack.ChartPath, ModConfig.CustomChartOffsetMs.Value);
                    }

                    // 원본 템플릿의 pParam 등 메타데이터를 유지한 채 노트 블록만 교체
                    var orig = __instance.mStep;
                    orig.nNote = parsed.nNote;
                    orig.nBlock = parsed.nBlock;
                    orig.pBlock = parsed.pBlock;

                    __instance.InitData();
                    Mod.Log.Msg($"[CustomChart] 주입 성공 (레인={orig.nNote}, 블록={orig.nBlock}, pParam 보존됨)");
                }
                catch (Exception ex)
                {
                    Mod.Log.Error($"[CustomChart] 파싱/주입 실패 → 원본 차트 유지: {ex}");
                }
            }
        }

        // 2. 커스텀 BGM / 프리뷰 주입: AudioManager.LoadMusicSound(string name, HashPrefix prefix, Action callback)
        [HarmonyPatch(typeof(AudioManager), nameof(AudioManager.LoadMusicSound))]
        internal static class AudioManager_LoadMusicSound_Patch
        {
            private static bool Prefix(AudioManager __instance, string name, GlobalFunc.HashPrefix prefix, Il2CppSystem.Action callback)
            {
                if (!ModConfig.CustomEnabled.Value)
                    return true;

                try
                {
                    bool isMainAudio = prefix == GlobalFunc.HashPrefix.AUDIO;
                    bool isPreview = prefix == GlobalFunc.HashPrefix.PREVIEWAUDIO && ModConfig.OverridePreviewAudio.Value;
                    if (!isMainAudio && !isPreview)
                        return true;

                    var pack = CustomPackManager.Resolve(name, GetCurrentPlayType(), GetCurrentLevel());
                    if (pack == null)
                        return true;

                    string audioPath = isPreview ? (pack.PreviewBgmPath ?? pack.BgmPath) : pack.BgmPath;
                    if (string.IsNullOrEmpty(audioPath))
                        return true;

                    var ilBytes = GetAudioIl2Cpp(audioPath, out string source);

                    // AudioManager.LoadMusic(p)는 MusicPlayer.Load(p, CREATESAMPLE|OPENMEMORY)와 같다(ISIL 확인).
                    // CREATESAMPLE은 곡 전체를 PCM으로 미리 풀어 수백 ms가 걸린다. 본게임은 로딩 화면 뒤라 괜찮고 판정 싱크도 공식 곡과
                    // 같아야 하므로 그대로 두고, 미리듣기만 스트림으로 연다(곡에 커서를 올릴 때마다 전체 디코딩하던 끊김 제거).
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    string mode;
                    var mp = isPreview && ModConfig.PreviewAudioStream.Value ? __instance.MusicPlayer : null;
                    if (mp != null)
                    {
                        mp.Load(ilBytes, Il2CppFMOD.MODE.CREATESTREAM | Il2CppFMOD.MODE.OPENMEMORY);
                        mode = "스트림";
                    }
                    else
                    {
                        __instance.LoadMusic(ilBytes);
                        mode = "전체 디코딩";
                    }
                    double fmodMs = sw.Elapsed.TotalMilliseconds;

                    Mod.Log.Msg($"[CustomBGM] '{pack.Title}' 오디오 주입: {Path.GetFileName(audioPath)} (prefix={prefix}, song={name}) — {source}, FMOD 열기({mode}) {fmodMs:0.0}ms");
                    callback?.Invoke();
                    return false;
                }
                catch (Exception ex)
                {
                    Mod.Log.Error($"[CustomBGM] 로드 실패 → 원본 오디오로 폴백: {ex}");
                    return true;
                }
            }
        }

        // 3a. 커스텀 BGA가 있을 때 공통 BGA(NeedCommonBGA)로 덮어쓰이지 않도록 차단
        [HarmonyPatch(typeof(BGAPlayer), nameof(BGAPlayer.NeedCommonBgaChecker))]
        internal static class BGAPlayer_NeedCommonBgaChecker_Patch
        {
            private static bool Prefix(BGAPlayer __instance, string name)
            {
                if (!ModConfig.CustomEnabled.Value)
                    return true;

                try
                {
                    var pack = CustomPackManager.Resolve(name, GetCurrentPlayType(), GetCurrentLevel());
                    if (pack == null || string.IsNullOrEmpty(pack.BgaPath))
                        return true;

                    __instance.NeedCommonBGA = false;
                    return false;
                }
                catch
                {
                    return true;
                }
            }
        }

        // 3b. 커스텀 BGA 주입: BGAPlayer.Load(string name)
        //     디스크의 평문 MP4/WebM을 file:// URL로 직접 스트리밍해 대용량(90MB+) BGA도 메모리 복사/프리징 없이 즉시 재생한다.
        [HarmonyPatch(typeof(BGAPlayer), nameof(BGAPlayer.Load))]
        internal static class BGAPlayer_Load_Patch
        {
            private static bool Prefix(BGAPlayer __instance, string name)
            {
                if (!ModConfig.CustomEnabled.Value)
                    return true;

                try
                {
                    var pack = CustomPackManager.Resolve(name, GetCurrentPlayType(), GetCurrentLevel());
                    if (pack == null || string.IsNullOrEmpty(pack.BgaPath))
                        return true;

                    var vp = __instance._BGAPlayer;
                    if (vp == null)
                    {
                        Mod.Log.Warning("[CustomBGA] _BGAPlayer가 null → 원본으로 폴백");
                        return true;
                    }

                    __instance.NeedCommonBGA = false;
                    string fileUrl = FileUrl(pack.BgaPath);
                    var prepareWatch = System.Diagnostics.Stopwatch.StartNew();
                    vp.source = VideoSource.Url;
                    vp.url = fileUrl;
                    // MP4 자체 오디오 트랙을 꺼서 FMOD BGM(music.ogg)과 소리가 겹치지 않도록 차단
                    vp.audioOutputMode = VideoAudioOutputMode.None;
                    Mod.Log.Msg($"[CustomBGA] '{pack.Title}' 직접 스트리밍 주입 (영상 음소거): {fileUrl}");

                    bool handled = false;
                    _bgaHandlers.Detach();
                    var onError = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<VideoPlayer.ErrorEventHandler>(
                        new Action<VideoPlayer, string>((s, msg) =>
                        {
                            if (handled) return;
                            handled = true;
                            Mod.Log.Warning($"[CustomBGA] VideoPlayer 오류: {msg}");
                            __instance.OnVideoError(s, msg);
                        }));
                    var onPrepared = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<VideoPlayer.EventHandler>(
                        new Action<VideoPlayer>(s =>
                        {
                            if (handled) return;
                            handled = true;
                            Mod.Log.Msg($"[CustomBGA] VideoPlayer 준비 완료 ({prepareWatch.Elapsed.TotalMilliseconds:0}ms)");
                            __instance.OnBGAPrepared(s);
                        }));
                    vp.add_errorReceived(onError);
                    vp.add_prepareCompleted(onPrepared);
                    _bgaHandlers.Player = vp;
                    _bgaHandlers.Error = onError;
                    _bgaHandlers.Prepared = onPrepared;
                    vp.Prepare();
                    return false;
                }
                catch (Exception ex)
                {
                    Mod.Log.Error($"[CustomBGA] 로드 실패 → 원본 BGA로 폴백: {ex}");
                    return true;
                }
            }
        }

        // 4a. GlobalFunc.DoesAssetExists("<songId>/previewVideo"):
        //     커스텀 곡은 Addressables에 없으므로 true를 반환해 선곡 화면 프리뷰 재생 경로를 열어준다.
        [HarmonyPatch(typeof(GlobalFunc), nameof(GlobalFunc.DoesAssetExists))]
        internal static class GlobalFunc_DoesAssetExists_Patch
        {
            private static bool Prefix(string __0, ref bool __result)
            {
                if (!ModConfig.CustomEnabled.Value || string.IsNullOrEmpty(__0))
                    return true;

                try
                {
                    const string previewVideoSuffix = "/previewVideo";
                    if (__0.EndsWith(previewVideoSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        string songId = __0.Substring(0, __0.Length - previewVideoSuffix.Length);
                        var pack = CustomPackManager.ResolveBySongId(songId);
                        if (pack != null && (!string.IsNullOrEmpty(pack.BgaPath) || !string.IsNullOrEmpty(pack.BgmPath)))
                        {
                            __result = true;
                            return false;
                        }
                    }
                }
                catch { }
                return true;
            }
        }

        // 4b. 선곡 화면 프리뷰 BGA 주입: EyeCatchUI.PreparePreviewVideo(string index)
        [HarmonyPatch(typeof(EyeCatchUI), nameof(EyeCatchUI.PreparePreviewVideo))]
        internal static class EyeCatchUI_PreparePreviewVideo_Patch
        {
            private static bool Prefix(EyeCatchUI __instance, string index)
            {
                if (!ModConfig.CustomEnabled.Value || __instance == null)
                    return true;

                try
                {
                    var pack = CustomPackManager.Resolve(index);
                    if (pack == null)
                        return true;

                    if (string.IsNullOrEmpty(pack.BgaPath))
                    {
                        // BGA는 없고 BGM만 있는 커스텀 곡은 비디오 로드 완료 플래그만 true로 두어 WaitForMediaAndPlay가 음악을 바로 재생하게 함
                        __instance._isVideoLoaded = true;
                        return false;
                    }

                    var vp = __instance.player;
                    if (vp == null)
                    {
                        Mod.Log.Warning("[PreviewBGA] EyeCatchUI.player가 null → 원본으로 폴백");
                        return true;
                    }

                    string fileUrl = FileUrl(pack.BgaPath);

                    // 같은 영상이 이미 준비된 상태로 남아 있으면(같은 곡으로 다시 돌아옴 등) Stop → Prepare로 디코더를 다시 열지 않는다
                    if (vp.source == VideoSource.Url && vp.isPrepared &&
                        string.Equals(vp.url, fileUrl, StringComparison.Ordinal))
                    {
                        vp.isLooping = true;
                        vp.audioOutputMode = VideoAudioOutputMode.None;
                        __instance._isVideoLoaded = true;
                        if (!vp.isPlaying) vp.Play();
                        Mod.Log.Msg($"[PreviewBGA] '{pack.Title}' 이미 준비된 프리뷰 영상 재사용 (Prepare 생략)");
                        return false;
                    }

                    var prepareWatch = System.Diagnostics.Stopwatch.StartNew();
                    vp.Stop();
                    vp.source = VideoSource.Url;
                    vp.url = fileUrl;
                    vp.isLooping = true;
                    // 선곡 프리뷰 MP4도 영상 오디오 음소거 (FMOD PREVIEWAUDIO만 재생)
                    vp.audioOutputMode = VideoAudioOutputMode.None;
                    __instance._isVideoLoaded = false;

                    bool handled = false;
                    _previewHandlers.Detach();
                    var onPrepared = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<VideoPlayer.EventHandler>(
                        new Action<VideoPlayer>(s =>
                        {
                            if (handled) return;
                            handled = true;
                            __instance._isVideoLoaded = true;
                            Mod.Log.Msg($"[PreviewBGA] 프리뷰 영상 준비 완료 ({prepareWatch.Elapsed.TotalMilliseconds:0}ms)");
                            try { s.Play(); } catch { }
                        }));
                    vp.add_prepareCompleted(onPrepared);
                    _previewHandlers.Player = vp;
                    _previewHandlers.Prepared = onPrepared;

                    vp.Prepare();
                    Mod.Log.Msg($"[PreviewBGA] '{pack.Title}' 선곡 프리뷰 BGA 주입: {Path.GetFileName(pack.BgaPath)}");
                    return false;
                }
                catch (Exception ex)
                {
                    Mod.Log.Warning($"[PreviewBGA] 로드 실패: {ex.Message}");
                    return true;
                }
            }
        }

        // 5. 선곡 화면 왼쪽 프리뷰 영역 자켓 이미지 주입: EyeCatchUI.UpdateSong(SongListContent content)
        [HarmonyPatch(typeof(EyeCatchUI), nameof(EyeCatchUI.UpdateSong))]
        internal static class EyeCatchUI_UpdateSong_Patch
        {
            private static void Postfix(EyeCatchUI __instance, SongListContent content)
            {
                try
                {
                    if (!ModConfig.CustomEnabled.Value || __instance == null || content == null)
                        return;

                    var pack = CustomPackManager.ResolveBySongId(Util.SongId(content.Song));
                    if (pack == null)
                        return;

                    // 커서가 커스텀 곡에 올라온 순간 오디오·차트를 백그라운드에서 미리 읽기 시작한다
                    PrefetchPack(pack);

                    if (string.IsNullOrEmpty(pack.JacketPath))
                        return;

                    var sprite = SongTableInjectorPatches.LoadOrGetJacket(pack.JacketPath);
                    if (sprite != null && __instance.eyeCatchImage != null)
                        __instance.eyeCatchImage.sprite = sprite;
                }
                catch { }
            }
        }
    }
}
