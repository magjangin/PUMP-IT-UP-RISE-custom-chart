using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Il2Cpp;
using MelonLoader.Utils;
using UnityEngine;

namespace PIURiseCustom
{
    /// <summary>
    /// SaveCustomKey/config.txt 기반 노트 비주얼/챌린지 패치:
    /// - NoteSway (노트 좌우 흔들림 + 판정선 근접 감쇠)
    /// - NoteSpeedChaos (레인별 또는 노트별 속도 배율 카오스)
    /// - Sudden / Hidden / LaneCoverSoftness
    /// - FadeIn / FadeOut
    /// - CustomNotes (게임 폴더 CustomNotes/ PNG로 노트 스프라이트 교체)
    /// </summary>
    internal static class NoteVisualPatches
    {
        // SpliteScript 인스턴스 ID별 원본 로컬 X/Y 위치 및 랜덤 시드 캐시
        // X/Y를 한 항목으로 묶어 프레임당 조회를 노트당 1회로 줄인다. Parent/Split을 같이 기억해 두었다가
        // 풀링으로 다른 라인·레인에 재사용된 노트면(값이 달라짐) 기준 위치를 다시 잡는다.
        private struct NoteBase
        {
            public float X, Y;
            public IntPtr Parent;
            public int Split;
        }
        private static readonly Dictionary<int, NoteBase> _noteBase = new();
        private static readonly Dictionary<int, float> _noteChaosMult = new();
        private static readonly float[] _laneChaosMult = new float[16];
        private static bool _laneChaosInitialized = false;

        // CustomNotes 스프라이트 캐시 (5키 기본 0..4: DL, UL, C, UR, DR)
        private static readonly Sprite[] _customLaneSprites = new Sprite[5];
        private static bool _customSpritesLoaded = false;

        // LaneCoverDebug 오버레이용 화면 좌표
        internal static float LastJudgeScreenY = -1f;
        internal static float LastLaneScreenHeight = 600f;
        internal static bool LastScrollUp = true; // PIU 기본은 아래→위 스크롤

        public static void ResetForNewStage()
        {
            _noteBase.Clear();
            _noteChaosMult.Clear();
            _laneChaosInitialized = false;
            LastJudgeScreenY = -1f;

            if (SaveCustomConfig.CustomNotes)
                EnsureCustomNoteSpritesLoaded();

            if (SaveCustomConfig.NoteSpeedChaos)
                InitLaneChaosMultipliers();
        }

        private static void InitLaneChaosMultipliers()
        {
            var rng = new System.Random(Environment.TickCount);
            float min = SaveCustomConfig.NoteSpeedChaosMin;
            float max = SaveCustomConfig.NoteSpeedChaosMax;
            for (int i = 0; i < _laneChaosMult.Length; i++)
                _laneChaosMult[i] = min + (float)rng.NextDouble() * (max - min);
            _laneChaosInitialized = true;
        }

        private static float GetChaosMultiplier(SpliteScript note)
        {
            if (!_laneChaosInitialized)
                InitLaneChaosMultipliers();

            if (SaveCustomConfig.NoteSpeedChaosPerLane)
            {
                int lane = Mathf.Clamp(note.Split, 0, _laneChaosMult.Length - 1);
                return _laneChaosMult[lane];
            }

            int id = note.GetInstanceID();
            if (!_noteChaosMult.TryGetValue(id, out float mult))
            {
                // 결정론적 해시로 노트별 배율 배정
                uint h = (uint)(id * 2654435761u);
                float t = (h & 0xFFFF) / 65535f;
                mult = Mathf.Lerp(SaveCustomConfig.NoteSpeedChaosMin, SaveCustomConfig.NoteSpeedChaosMax, t);
                _noteChaosMult[id] = mult;
            }
            return mult;
        }

        private static void EnsureCustomNoteSpritesLoaded()
        {
            if (_customSpritesLoaded)
                return;
            _customSpritesLoaded = true;

            string dir = Path.Combine(MelonEnvironment.GameRootDirectory, "CustomNotes");
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { }
                return;
            }

            // 5키 레인별 파일명 후보 (PIU 명칭 + Sixtar Gate Blue/Red/Yellow 호환)
            string[][] candidates =
            {
                new[] { "0.png", "DL.png", "DownLeft.png", "Blue.png" },
                new[] { "1.png", "UL.png", "UpLeft.png", "Red.png" },
                new[] { "2.png", "C.png", "Center.png", "Yellow.png", "Gate.png" },
                new[] { "3.png", "UR.png", "UpRight.png", "Red.png" },
                new[] { "4.png", "DR.png", "DownRight.png", "Blue.png" },
            };

            int loaded = 0;
            for (int i = 0; i < 5; i++)
            {
                foreach (string name in candidates[i])
                {
                    string path = Path.Combine(dir, name);
                    if (!File.Exists(path)) continue;
                    var sp = LoadSpriteFromPng(path);
                    if (sp != null)
                    {
                        _customLaneSprites[i] = sp;
                        loaded++;
                        break;
                    }
                }
            }

            if (loaded > 0)
                Mod.Log?.Msg($"[CustomNotes] {dir} 에서 {loaded}개 레인 노트 스킨 로드 완료");
        }

        private static Sprite LoadSpriteFromPng(string path)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, bytes, false))
                    return null;
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                var sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                sp.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return sp;
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyCustomNoteSprite(SpliteScript note)
        {
            if (!SaveCustomConfig.CustomNotes || note == null)
                return;

            EnsureCustomNoteSpritesLoaded();
            int lane = note.Split % 5;
            if (lane < 0) lane += 5;
            var customSp = _customLaneSprites[lane];
            if (customSp == null)
                return;

            // SpliteScript.drawAnim()의 Animator가 스프라이트를 매 프레임 덮어쓰지 않도록 비활성화
            if (note._anim != null && note._anim.enabled)
                note._anim.enabled = false;

            if (note._sp != null && note._sp.sprite != customSp)
                note._sp.sprite = customSp;
        }

        // CustomNotes: 노트 초기화 시 스프라이트 교체
        [HarmonyPatch(typeof(SpliteScript), nameof(SpliteScript.Init))]
        internal static class SpliteScript_Init_CustomNotes_Patch
        {
            private static void Postfix(SpliteScript __instance)
            {
                try
                {
                    ApplyCustomNoteSprite(__instance);
                }
                catch { }
            }
        }

        // NoteSway / NoteSpeedChaos / Sudden / Hidden / FadeIn / FadeOut / CustomNotes (일반 노트)
        [HarmonyPatch(typeof(SpliteScript), "LateUpdate")]
        internal static class SpliteScript_LateUpdate_Visual_Patch
        {
            private static void Postfix(SpliteScript __instance)
            {
                ApplyVisuals(__instance, null);
            }
        }

        // 롱노트(LongSpliteScript)는 LateUpdate를 오버라이드하므로 별도 패치로 동일 비주얼 적용
        [HarmonyPatch(typeof(LongSpliteScript), "LateUpdate")]
        internal static class LongSpliteScript_LateUpdate_Visual_Patch
        {
            private static void Postfix(LongSpliteScript __instance)
            {
                ApplyVisuals(__instance, __instance);
            }
        }

        private static void ApplyVisuals(SpliteScript note, LongSpliteScript longNote)
        {
            if (note == null)
                return;

            bool needSway = SaveCustomConfig.NoteSway;
            bool needChaos = SaveCustomConfig.NoteSpeedChaos && longNote == null;
            bool needCover = SaveCustomConfig.Sudden > 0.01f || SaveCustomConfig.Hidden > 0.01f;
            bool needFade = SaveCustomConfig.FadeIn || SaveCustomConfig.FadeOut;
            bool needCustom = SaveCustomConfig.CustomNotes;

            if (!needSway && !needChaos && !needCover && !needFade && !needCustom && !SaveCustomConfig.LaneCoverDebug)
                return;

            try
            {
                if (needCustom)
                    ApplyCustomNoteSprite(note);

                if (!needSway && !needChaos && !needCover && !needFade && !SaveCustomConfig.LaneCoverDebug)
                    return;

                var playBase = note._playBase ?? GameplayOptionPatches.ActivePlayBase;
                var tr = note.transform;
                var parentLine = note.pParentLine;
                int id = note.GetInstanceID();

                // 원본 로컬 X/Y 기록 (프레임 누적 폭주 방지용)
                Vector3 lp = tr.localPosition;
                IntPtr parentPtr = parentLine != null ? parentLine.Pointer : IntPtr.Zero;
                if (!_noteBase.TryGetValue(id, out NoteBase nb) || nb.Parent != parentPtr || nb.Split != note.Split)
                {
                    nb = new NoteBase { X = lp.x, Y = lp.y, Parent = parentPtr, Split = note.Split };
                    _noteBase[id] = nb;
                }
                float origX = nb.X;
                float origY = nb.Y;

                // 판정선 월드 Y 추정 (ArrowMaker 또는 PlayBase.ARROW_Y)
                float judgeWorldY = playBase != null ? playBase.ARROW_Y : 0f;
                if (parentLine != null && parentLine.TargetArrow != null)
                    judgeWorldY = parentLine.TargetArrow.transform.position.y;

                // 1. NoteSway (좌우 흔들림, 판정 무관)
                if (needSway)
                {
                    float ampWorld = SaveCustomConfig.NoteSwayAmplitude / 100f; // PPU = 100
                    float phase = (id * 0.37f) + (note.Split * 0.9f);
                    float wave = Mathf.Sin(Time.time * SaveCustomConfig.NoteSwaySpeed * Mathf.PI * 2f + phase);

                    float damp = 1f;
                    if (SaveCustomConfig.NoteSwayDamping && parentLine != null && playBase != null && playBase._player != null)
                    {
                        float remainSec = (parentLine._judgeTime - playBase._player.msCurTime) / 1000f;
                        if (remainSec < SaveCustomConfig.NoteSwayDampingTime)
                            damp = Mathf.Clamp01(remainSec / SaveCustomConfig.NoteSwayDampingTime);
                    }

                    lp.x = origX + wave * ampWorld * damp;
                    tr.localPosition = lp;
                }

                // 2. NoteSpeedChaos (매 프레임 origY로 먼저 복원 후 배율 적용 → 누적 폭주 방지)
                if (needChaos)
                {
                    lp = tr.localPosition;
                    lp.y = origY;
                    tr.localPosition = lp;

                    float mult = GetChaosMultiplier(note);
                    Vector3 wp = tr.position;
                    float dy = wp.y - judgeWorldY;
                    wp.y = judgeWorldY + dy * mult;
                    tr.position = wp;
                }

                // 3. Sudden / Hidden / FadeIn / FadeOut 알파 계산
                if (needCover || needFade || SaveCustomConfig.LaneCoverDebug)
                {
                    var cam = Camera.main;
                    if (cam != null)
                    {
                        Vector3 wp = tr.position;
                        Vector3 noteScreen = cam.WorldToScreenPoint(wp);
                        Vector3 judgeScreen = cam.WorldToScreenPoint(new Vector3(wp.x, judgeWorldY, wp.z));

                        // GUI 좌표계(상단=0, 하단=Screen.height)로 변환
                        float judgeGuiY = Screen.height - judgeScreen.y;
                        float noteGuiY = Screen.height - noteScreen.y;

                        // 스크롤 방향 판정: 판정선이 화면 상단 절반에 있으면 아래→위 스크롤
                        bool scrollUp = judgeGuiY < Screen.height * 0.5f;
                        float laneHeightPx = scrollUp
                            ? Mathf.Max(200f, Screen.height - judgeGuiY)
                            : Mathf.Max(200f, judgeGuiY);

                        LastJudgeScreenY = judgeGuiY;
                        LastLaneScreenHeight = laneHeightPx;
                        LastScrollUp = scrollUp;

                        // u: 판정선 = 0.0, 노트가 나오는 화면 끝 = 1.0
                        float distPx = scrollUp ? (noteGuiY - judgeGuiY) : (judgeGuiY - noteGuiY);
                        float u = distPx / laneHeightPx;

                        float alpha = 1f;
                        float softU = SaveCustomConfig.LaneCoverSoftness / laneHeightPx;

                        // Sudden (스폰쪽 가림: u > 1 - Sudden/100)
                        if (SaveCustomConfig.Sudden > 0.01f)
                        {
                            float suddenEdge = 1f - (SaveCustomConfig.Sudden / 100f);
                            float aSudden = softU <= 0.0001f
                                ? (u > suddenEdge ? 0f : 1f)
                                : 1f - Mathf.Clamp01((u - (suddenEdge - softU * 0.5f)) / softU);
                            alpha = Mathf.Min(alpha, aSudden);
                        }

                        // Hidden (판정선쪽 가림: u < Hidden/100)
                        if (SaveCustomConfig.Hidden > 0.01f)
                        {
                            float hiddenEdge = SaveCustomConfig.Hidden / 100f;
                            float aHidden = softU <= 0.0001f
                                ? (u < hiddenEdge ? 0f : 1f)
                                : Mathf.Clamp01((u - (hiddenEdge - softU * 0.5f)) / softU);
                            alpha = Mathf.Min(alpha, aHidden);
                        }

                        // FadeIn (u가 FadeInStart%에서 FadeInStart-FadeInLength%로 내려올 때 0→1)
                        if (SaveCustomConfig.FadeIn)
                        {
                            float startU = SaveCustomConfig.FadeInStart / 100f;
                            float endU = (SaveCustomConfig.FadeInStart - SaveCustomConfig.FadeInLength) / 100f;
                            float aFi = Mathf.Clamp01((startU - u) / Mathf.Max(0.001f, startU - endU));
                            alpha = Mathf.Min(alpha, aFi);
                        }

                        // FadeOut (u가 FadeOutStart%에서 FadeOutStart-FadeOutLength%로 내려올 때 1→0)
                        if (SaveCustomConfig.FadeOut)
                        {
                            float startU = SaveCustomConfig.FadeOutStart / 100f;
                            float endU = (SaveCustomConfig.FadeOutStart - SaveCustomConfig.FadeOutLength) / 100f;
                            float aFo = Mathf.Clamp01((u - endU) / Mathf.Max(0.001f, startU - endU));
                            alpha = Mathf.Min(alpha, aFo);
                        }

                        if (needCover || needFade)
                        {
                            if (note._sp != null)
                            {
                                Color c = note._sp.color;
                                c.a = alpha;
                                note._sp.color = c;
                            }
                            if (note._additive != null)
                            {
                                Color c = note._additive.color;
                                c.a = alpha;
                                note._additive.color = c;
                            }
                            if (longNote != null && longNote._renderers != null)
                            {
                                var rends = longNote._renderers;
                                for (int i = 0; i < rends.Length; i++)
                                {
                                    var sr = rends[i];
                                    if (sr == null) continue;
                                    Color c = sr.color;
                                    c.a = alpha;
                                    sr.color = c;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }
    }
}
