using System;
using HarmonyLib;
using Il2Cpp;
using Il2CppPIUUtil;
using UnityEngine;

namespace PIURiseCustom
{
    /// <summary>
    /// 식스타게이트 스타일의 독립 'CUSTOM' 채널 탭을 선곡 화면에 추가한다.
    /// - 상단 채널 탭: ALL TUNES | CUSTOM | LEGACY | FIESTA ...
    /// - CUSTOM 채널 선택 시 hwa/ 하위 폴더의 커스텀 곡들만 모아서 표시한다.
    /// </summary>
    internal static class CustomChannelPatches
    {
        public const CommonEnums.Channel CUSTOM_CHANNEL = (CommonEnums.Channel)99;

        private static Sprite _customLabelSelected;
        private static Sprite _customLabelUnselected;

        private static Sprite GetCustomChannelSprite(bool selected)
        {
            if (selected && _customLabelSelected != null) return _customLabelSelected;
            if (!selected && _customLabelUnselected != null) return _customLabelUnselected;

            // 5x7 비트맵 폰트로 "CUSTOM" 텍스트 텍스처 생성 (256x64, 투명 배경)
            const int w = 256;
            const int h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color32[w * h];
            // 투명 초기화
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(0, 0, 0, 0);

            Color32 fg = selected
                ? new Color32(255, 255, 255, 255)
                : new Color32(210, 225, 245, 235);
            Color32 shadow = new Color32(0, 0, 0, 160);

            string[] glyphsC = { "01110", "10001", "10000", "10000", "10000", "10001", "01110" };
            string[] glyphsU = { "10001", "10001", "10001", "10001", "10001", "10001", "01110" };
            string[] glyphsS = { "01111", "10000", "10000", "01110", "00001", "00001", "11110" };
            string[] glyphsT = { "11111", "00100", "00100", "00100", "00100", "00100", "00100" };
            string[] glyphsO = { "01110", "10001", "10001", "10001", "10001", "10001", "01110" };
            string[] glyphsM = { "10001", "11011", "10101", "10101", "10001", "10001", "10001" };
            string[][] word = { glyphsC, glyphsU, glyphsS, glyphsT, glyphsO, glyphsM };

            int scale = 5;
            int charW = 5 * scale;
            int gap = 8;
            int totalW = word.Length * charW + (word.Length - 1) * gap;
            int startX = (w - totalW) / 2;
            int startY = (h - 7 * scale) / 2;

            for (int gi = 0; gi < word.Length; gi++)
            {
                int ox = startX + gi * (charW + gap);
                string[] g = word[gi];
                for (int row = 0; row < 7; row++)
                {
                    for (int col = 0; col < 5; col++)
                    {
                        if (g[row][col] != '1') continue;
                        // 약간의 이탤릭 기울기(PIU 채널 탭 스타일)
                        int skew = (6 - row) / 2;
                        int px = ox + col * scale + skew;
                        int py = h - 1 - (startY + row * scale);

                        for (int dy = 0; dy < scale; dy++)
                        {
                            for (int dx = 0; dx < scale; dx++)
                            {
                                int x = px + dx;
                                int y = py - dy;
                                // 그림자
                                if (x + 2 < w && y - 2 >= 0)
                                    pixels[(y - 2) * w + (x + 2)] = shadow;
                                if (x >= 0 && x < w && y >= 0 && y < h)
                                    pixels[y * w + x] = fg;
                            }
                        }
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;

            var spr = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            spr.hideFlags = HideFlags.DontUnloadUnusedAsset;

            if (selected) _customLabelSelected = spr;
            else _customLabelUnselected = spr;
            return spr;
        }

        private static Il2CppSystem.Collections.Generic.List<SongListContent> BuildCustomContentList(Mk2SongLoader loader)
        {
            var result = new Il2CppSystem.Collections.Generic.List<SongListContent>();
            if (loader == null || loader._contents == null)
                return result;

            for (int i = 0; i < loader._contents.Count; i++)
            {
                var item = loader._contents[i];
                if (item != null && CustomPackManager.IsCatalogSongId(item.Song.ID))
                    result.Add(item);
            }
            return result;
        }

        // 1. CacheAllChannels 이후 _channelCache[CUSTOM_CHANNEL] 채우기
        [HarmonyPatch(typeof(Mk2SongLoader), nameof(Mk2SongLoader.CacheAllChannels))]
        internal static class Mk2SongLoader_CacheAllChannels_Patch
        {
            private static void Postfix(Mk2SongLoader __instance)
            {
                if (!ModConfig.CustomEnabled.Value || __instance == null || __instance._channelCache == null)
                    return;

                var list = BuildCustomContentList(__instance);
                __instance._channelCache[CUSTOM_CHANNEL] = list;
                Mod.Log.Msg($"[CustomChannel] CUSTOM 채널 캐시 등록 완료: {list.Count}곡");
            }
        }

        // 2. IsEmpty(CUSTOM_CHANNEL)
        [HarmonyPatch(typeof(Mk2SongLoader), nameof(Mk2SongLoader.IsEmpty))]
        internal static class Mk2SongLoader_IsEmpty_Patch
        {
            private static bool Prefix(CommonEnums.Channel channel, ref bool __result)
            {
                if (channel != CUSTOM_CHANNEL)
                    return true;

                __result = !ModConfig.CustomEnabled.Value || CustomPackManager.CatalogSongs.Count == 0;
                return false;
            }
        }

        // 3. GetChannelSongList(CUSTOM_CHANNEL)
        [HarmonyPatch(typeof(Mk2SongLoader), nameof(Mk2SongLoader.GetChannelSongList))]
        internal static class Mk2SongLoader_GetChannelSongList_Patch
        {
            private static bool Prefix(Mk2SongLoader __instance, CommonEnums.Channel channel, ref Il2CppSystem.Collections.Generic.List<SongListContent> __result)
            {
                if (channel != CUSTOM_CHANNEL)
                    return true;

                if (__instance._channelCache != null && __instance._channelCache.ContainsKey(CUSTOM_CHANNEL))
                    __result = __instance._channelCache[CUSTOM_CHANNEL];
                else
                    __result = BuildCustomContentList(__instance);

                return false;
            }
        }

        // 4. 채널 탭 UI에 CUSTOM 채널 삽입 (ALLTUNES 바로 다음 위치)
        [HarmonyPatch(typeof(NewChannelListUI), nameof(NewChannelListUI.InitializeListForBoxes))]
        internal static class NewChannelListUI_InitializeListForBoxes_Patch
        {
            private static void Prefix(NewChannelListUI __instance)
            {
                if (!ModConfig.CustomEnabled.Value || CustomPackManager.CatalogSongs.Count == 0)
                    return;

                var list = __instance.channelList;
                if (list == null)
                    return;

                bool exists = false;
                int allTunesIdx = -1;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] == CUSTOM_CHANNEL) exists = true;
                    if (list[i] == CommonEnums.Channel.ALLTUNES) allTunesIdx = i;
                }

                if (!exists)
                {
                    int insertAt = allTunesIdx >= 0 ? allTunesIdx + 1 : 0;
                    list.Insert(insertAt, CUSTOM_CHANNEL);
                    Mod.Log.Msg($"[CustomChannel] NewChannelListUI.channelList[{insertAt}]에 CUSTOM 탭 추가 완료");
                }
            }
        }

        // 5. 채널 박스 스프라이트: CUSTOM_CHANNEL이면 자체 생성한 "CUSTOM" 스프라이트 적용
        [HarmonyPatch(typeof(NewChannelListBox), nameof(NewChannelListBox.UpdateChannelSprite))]
        internal static class NewChannelListBox_UpdateChannelSprite_Patch
        {
            private static bool Prefix(NewChannelListBox __instance, CommonEnums.Channel channel)
            {
                if (channel != CUSTOM_CHANNEL)
                    return true;

                try
                {
                    if (__instance.textImage != null)
                    {
                        __instance.textImage.sprite = GetCustomChannelSprite(true);
                        __instance.textImage.preserveAspect = true;
                    }
                    if (__instance.unTextImage != null)
                    {
                        __instance.unTextImage.sprite = GetCustomChannelSprite(false);
                        __instance.unTextImage.preserveAspect = true;
                    }
                    if (__instance.bgImage != null && __instance._normalSpr != null)
                    {
                        __instance.bgImage.sprite = __instance._normalSpr;
                    }
                }
                catch (Exception ex)
                {
                    Mod.Log.Warning($"[CustomChannel] UpdateChannelSprite 오류: {ex.Message}");
                }

                return false;
            }
        }

        // 6. 선택된 채널 애니메이션 전환 시에도 CUSTOM 라벨 스프라이트 유지
        [HarmonyPatch(typeof(NewChannelListUI), nameof(NewChannelListUI.StartSelectedChannelAni))]
        internal static class NewChannelListUI_StartSelectedChannelAni_Patch
        {
            private static void Postfix(NewChannelListUI __instance, CommonEnums.Channel channel)
            {
                if (channel != CUSTOM_CHANNEL)
                    return;

                try
                {
                    var spr = GetCustomChannelSprite(true);
                    if (__instance._channelTextImageB != null)
                    {
                        __instance._channelTextImageB.sprite = spr;
                        __instance._channelTextImageB.preserveAspect = true;
                    }
                    if (__instance._channelTextImageC != null)
                    {
                        __instance._channelTextImageC.sprite = spr;
                        __instance._channelTextImageC.preserveAspect = true;
                    }
                }
                catch { }
            }
        }
    }
}
