using System;
using System.IO;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPIUMobileStepDLL;
using UnityEngine.Video;

namespace PIURiseCustom
{
    internal static class CustomMediaPatches
    {
        // PlayBase.LoadStep에서 HWA_ 커스텀 곡을 템플릿 ID로 우회 통과시킬 때 실제 팩을 전달하는 슬롯
        private static CustomPack _pendingCustomPack;

        private static string GetCurrentSongId()
        {
            try
            {
                var pd = PlayerData.Instance;
                if (pd != null)
                    return pd.Song.ID;
            }
            catch { }
            return null;
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

        // 0. PlayBase.LoadStep(songId, lv, playType): HWA 독립 곡의 팩을 Step.Load(byte[])에 전달한다.
        [HarmonyPatch(typeof(PlayBase), nameof(PlayBase.LoadStep))]
        internal static class PlayBase_LoadStep_Patch
        {
            private static void Prefix(ref string songId, ref int lv, ref string playType)
            {
                _pendingCustomPack = null;
                if (!ModConfig.CustomEnabled.Value)
                    return;

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

                try
                {
                    Mod.Log.Msg($"[CustomBGM] '{pack.Title}' 오디오 주입: {Path.GetFileName(audioPath)} (prefix={prefix}, song={name})");
                    byte[] rawBytes = File.ReadAllBytes(audioPath);
                    var ilBytes = new Il2CppStructArray<byte>(rawBytes);

                    __instance.LoadMusic(ilBytes);
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

                var pack = CustomPackManager.Resolve(name, GetCurrentPlayType(), GetCurrentLevel());
                if (pack == null || string.IsNullOrEmpty(pack.BgaPath))
                    return true;

                __instance.NeedCommonBGA = false;
                return false;
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

                var pack = CustomPackManager.Resolve(name, GetCurrentPlayType(), GetCurrentLevel());
                if (pack == null || string.IsNullOrEmpty(pack.BgaPath))
                    return true;

                try
                {
                    var vp = __instance._BGAPlayer;
                    if (vp == null)
                    {
                        Mod.Log.Warning("[CustomBGA] _BGAPlayer가 null → 원본으로 폴백");
                        return true;
                    }

                    __instance.NeedCommonBGA = false;
                    string fileUrl = new Uri(Path.GetFullPath(pack.BgaPath)).AbsoluteUri;
                    vp.source = VideoSource.Url;
                    vp.url = fileUrl;
                    // MP4 자체 오디오 트랙을 꺼서 FMOD BGM(music.ogg)과 소리가 겹치지 않도록 차단
                    vp.audioOutputMode = VideoAudioOutputMode.None;
                    Mod.Log.Msg($"[CustomBGA] '{pack.Title}' 직접 스트리밍 주입 (영상 음소거): {fileUrl}");

                    bool handled = false;
                    vp.add_errorReceived(Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<VideoPlayer.ErrorEventHandler>(
                        new Action<VideoPlayer, string>((s, msg) =>
                        {
                            if (handled) return;
                            handled = true;
                            Mod.Log.Warning($"[CustomBGA] VideoPlayer 오류: {msg}");
                            __instance.OnVideoError(s, msg);
                        })));
                    vp.add_prepareCompleted(Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<VideoPlayer.EventHandler>(
                        new Action<VideoPlayer>(s =>
                        {
                            if (handled) return;
                            handled = true;
                            Mod.Log.Msg("[CustomBGA] VideoPlayer 준비 완료");
                            __instance.OnBGAPrepared(s);
                        })));
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

                var pack = CustomPackManager.Resolve(index);
                if (pack == null)
                    return true;

                if (string.IsNullOrEmpty(pack.BgaPath))
                {
                    // BGA는 없고 BGM만 있는 커스텀 곡은 비디오 로드 완료 플래그만 true로 두어 WaitForMediaAndPlay가 음악을 바로 재생하게 함
                    __instance._isVideoLoaded = true;
                    return false;
                }

                try
                {
                    var vp = __instance.player;
                    if (vp == null)
                    {
                        Mod.Log.Warning("[PreviewBGA] EyeCatchUI.player가 null → 원본으로 폴백");
                        return true;
                    }

                    string fileUrl = new Uri(Path.GetFullPath(pack.BgaPath)).AbsoluteUri;
                    vp.Stop();
                    vp.source = VideoSource.Url;
                    vp.url = fileUrl;
                    vp.isLooping = true;
                    // 선곡 프리뷰 MP4도 영상 오디오 음소거 (FMOD PREVIEWAUDIO만 재생)
                    vp.audioOutputMode = VideoAudioOutputMode.None;
                    __instance._isVideoLoaded = false;

                    bool handled = false;
                    vp.add_prepareCompleted(Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<VideoPlayer.EventHandler>(
                        new Action<VideoPlayer>(s =>
                        {
                            if (handled) return;
                            handled = true;
                            __instance._isVideoLoaded = true;
                            try { s.Play(); } catch { }
                        })));

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
                if (!ModConfig.CustomEnabled.Value || __instance == null || content == null)
                    return;

                var pack = CustomPackManager.ResolveBySongId(content.Song.ID);
                if (pack == null || string.IsNullOrEmpty(pack.JacketPath))
                    return;

                var sprite = SongTableInjectorPatches.LoadOrGetJacket(pack.JacketPath);
                if (sprite != null && __instance.eyeCatchImage != null)
                    __instance.eyeCatchImage.sprite = sprite;
            }
        }
    }
}
