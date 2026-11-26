using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
                int lane = Math.Clamp(note.Split, 0, _laneChaosMult.Length - 1);
                return _laneChaosMult[lane];
            }

            int id = note.GetInstanceID();
            if (!_noteChaosMult.TryGetValue(id, out float mult))
            {
                // 결정론적 해시로 노트별 배율 배정
                uint h = (uint)(id * 2654435761u);
                float t = (h & 0xFFFF) / 65535f;
                mult = SaveCustomConfig.NoteSpeedChaosMin + (SaveCustomConfig.NoteSpeedChaosMax - SaveCustomConfig.NoteSpeedChaosMin) * t;
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
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }
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
            // 호출부(Init Postfix의 __instance, ApplyVisuals)가 이미 null이 아닌 노트를 넘긴다 → 관리 비교만(v0.3.15, 노트마다·프레임마다 경로)
            if (!SaveCustomConfig.CustomNotes || note is null)
                return;

            EnsureCustomNoteSpritesLoaded();
            int lane = note.Split % 5;
            if (lane < 0) lane += 5;
            var customSp = _customLaneSprites[lane]; // 우리가 만든 캐시(DontUnloadUnusedAsset) → 관리 비교
            if (customSp is null)
                return;

            // SpliteScript.drawAnim()의 Animator가 스프라이트를 매 프레임 덮어쓰지 않도록 비활성화
            var anim = note._anim;
            if (anim is not null && anim.enabled)
                anim.enabled = false;

            // 스프라이트 비교는 Unity op_Inequality(IL2CPP 호출) 대신 네이티브 포인터 비교
            var sp = note._sp;
            if (sp is not null)
            {
                var cur = sp.sprite;
                if (cur is null || cur.Pointer != customSp.Pointer)
                    sp.sprite = customSp;
            }
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

        // NoteSway / NoteSpeedChaos / Sudden / Hidden / FadeIn / FadeOut / CustomNotes (일반 노트·롱노트)
        // ★ 매 프레임 × 화면의 모든 노트마다 불리는 경로라, 비주얼 기능이 하나도 켜져 있지 않으면 훅 자체를 걸지 않는다(v0.3.3).
        //   IL2CPP Harmony 훅은 본문이 바로 return해도 호출마다 트램펄린·인자 마샬링 비용이 든다.
        //   → [HarmonyPatch] 속성 없이 LateUpdateHooks.Sync가 MainStage.Start(config 핫리로드 직후)마다 붙이거나 뗀다.
        internal static class LateUpdateHooks
        {
            private static void ShortPostfix(SpliteScript __instance) => ApplyVisuals(__instance, null);
            private static void LongPostfix(LongSpliteScript __instance) => ApplyVisuals(__instance, __instance);

            private static bool _attached;
            private static bool _resolveFailed;
            private static MethodInfo _shortTarget, _longTarget, _shortPatch, _longPatch;

            internal static bool Attached => _attached;

            internal static bool AnyFeatureEnabled()
            {
                return SaveCustomConfig.NoteSway
                    || SaveCustomConfig.NoteSpeedChaos
                    || SaveCustomConfig.Sudden > 0.01f || SaveCustomConfig.Hidden > 0.01f
                    || SaveCustomConfig.FadeIn || SaveCustomConfig.FadeOut
                    || SaveCustomConfig.CustomNotes
                    || SaveCustomConfig.LaneCoverDebug;
            }

            internal static void Sync(HarmonyLib.Harmony harmony)
            {
                if (harmony == null || _resolveFailed)
                    return;

                bool want = AnyFeatureEnabled();
                if (want == _attached)
                    return;

                try
                {
                    if (_shortTarget == null)
                    {
                        // LongSpliteScript는 LateUpdate를 오버라이드하므로 각자 선언한 메서드를 정확히 집는다
                        _shortTarget = AccessTools.DeclaredMethod(typeof(SpliteScript), "LateUpdate");
                        _longTarget = AccessTools.DeclaredMethod(typeof(LongSpliteScript), "LateUpdate");
                        _shortPatch = AccessTools.Method(typeof(LateUpdateHooks), nameof(ShortPostfix));
                        _longPatch = AccessTools.Method(typeof(LateUpdateHooks), nameof(LongPostfix));
                        if (_shortTarget == null)
                        {
                            _resolveFailed = true;
                            Mod.Log.Warning("[NoteVisual] SpliteScript.LateUpdate를 찾지 못해 노트 비주얼 기능(흔들림·카오스·서든/히든·페이드·커스텀 노트 갱신)을 끕니다 — 게임 업데이트 확인 필요");
                            return;
                        }
                        if (_longTarget == null)
                            Mod.Log.Warning("[NoteVisual] LongSpliteScript.LateUpdate를 찾지 못해 롱노트에는 비주얼 기능이 적용되지 않습니다");
                    }

                    if (want)
                    {
                        harmony.Patch(_shortTarget, postfix: new HarmonyMethod(_shortPatch));
                        if (_longTarget != null)
                            harmony.Patch(_longTarget, postfix: new HarmonyMethod(_longPatch));
                        _attached = true;
                        Mod.Log.Msg("[NoteVisual] 노트 비주얼 기능이 켜져 있어 LateUpdate 훅을 붙였습니다");
                    }
                    else
                    {
                        harmony.Unpatch(_shortTarget, _shortPatch);
                        if (_longTarget != null)
                            harmony.Unpatch(_longTarget, _longPatch);
                        _attached = false;
                        Mod.Log.Msg("[NoteVisual] 노트 비주얼 기능이 모두 꺼져 LateUpdate 훅을 뗐습니다");
                    }
                }
                catch (Exception ex)
                {
                    // 붙이다 실패하면 반복 시도로 로그가 쌓이지 않게 이번 실행에서는 포기한다(나머지 기능은 영향 없음)
                    _resolveFailed = true;
                    // 일반 노트만 붙고 롱노트에서 실패한 경우처럼 반쯤 붙은 상태로 남으면, 기능을 꺼도 다시는 떼지 못하므로 붙은 쪽을 되돌린다
                    if (want)
                    {
                        try { harmony.Unpatch(_shortTarget, _shortPatch); } catch { }
                        if (_longTarget != null) { try { harmony.Unpatch(_longTarget, _longPatch); } catch { } }
                        _attached = false;
                    }
                    Mod.Log.Warning($"[NoteVisual] LateUpdate 훅 {(want ? "부착" : "해제")} 실패 — 노트 비주얼 기능 비활성: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        // 매 프레임 한 번만 읽으면 되는 Unity 값(Time.time, Camera.main, Screen.height) + 판정선 화면 Y 캐시.
        // 노트마다 다시 읽으면 노트 수만큼 IL2CPP 호출이 난다.
        private static int _frameCacheIndex = -1;
        private static float _frameTime;
        private static Camera _frameCam;
        private static int _frameScreenH;
        private static bool _frameJudgeValid;
        private static float _frameJudgeWorldY;
        private static float _frameJudgeGuiY;
        private static bool _frameScrollUp;
        private static float _frameLaneH;

        private static void RefreshFrameCache()
        {
            int frame = Mod.FrameIndex;
            if (frame == _frameCacheIndex)
                return;
            _frameCacheIndex = frame;
            _frameTime = Time.time;
            _frameCam = Camera.main;
            _frameScreenH = Screen.height;
            _frameJudgeValid = false;
        }

        // 노트마다·프레임마다 2~N번 불린다. 렌더러는 살아 있는 노트의 자식이라 파괴 감지(Unity == null, IL2CPP 호출) 대신
        // 관리 비교만 한다(v0.3.15). 파괴된 렌더러면 color 접근이 예외를 내고 ApplyVisuals의 try가 그 노트만 건너뛴다.
        private static void SetSpriteAlphaIfChanged(SpriteRenderer sr, float alpha)
        {
            if (sr is null) return;
            Color c = sr.color;
            if (MathF.Abs(c.a - alpha) <= 0.002f) return;
            c.a = alpha;
            sr.color = c;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        private static void ApplyVisuals(SpliteScript note, LongSpliteScript longNote)
        {
            if (note is null)
                return;

            bool needSway = SaveCustomConfig.NoteSway;
            // longNote는 일반 노트일 때 우리가 넘기는 관리 null이다 → Unity 비교(IL2CPP 호출) 대신 관리 비교(노트마다·프레임마다 불리는 경로)
            bool needChaos = SaveCustomConfig.NoteSpeedChaos && longNote is null;
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
                RefreshFrameCache();
                var tr = note.transform;
                var parentLine = note.pParentLine;
                int id = note.GetInstanceID();

                // 원본 로컬 X/Y 기록 (프레임 누적 폭주 방지용)
                Vector3 lp = tr.localPosition;
                // Unity 객체 null 비교(op_Inequality)는 IL2CPP 호출이라 노트당 한 번만 한다
                bool hasParent = parentLine != null;
                bool hasPlayBase = playBase != null;
                IntPtr parentPtr = hasParent ? parentLine.Pointer : IntPtr.Zero;
                if (!_noteBase.TryGetValue(id, out NoteBase nb) || nb.Parent != parentPtr || nb.Split != note.Split)
                {
                    nb = new NoteBase { X = lp.x, Y = lp.y, Parent = parentPtr, Split = note.Split };
                    _noteBase[id] = nb;
                }
                float origX = nb.X;
                float origY = nb.Y;

                // 판정선 월드 Y 추정 (ArrowMaker 또는 PlayBase.ARROW_Y)
                float judgeWorldY = hasPlayBase ? playBase.ARROW_Y : 0f;
                if (hasParent && parentLine.TargetArrow != null)
                    judgeWorldY = parentLine.TargetArrow.transform.position.y;

                // 1. NoteSway (좌우 흔들림, 판정 무관)
                if (needSway)
                {
                    float ampWorld = SaveCustomConfig.NoteSwayAmplitude / 100f; // PPU = 100
                    float phase = (id * 0.37f) + (note.Split * 0.9f);
                    float wave = MathF.Sin(_frameTime * SaveCustomConfig.NoteSwaySpeed * MathF.PI * 2f + phase);

                    float damp = 1f;
                    if (SaveCustomConfig.NoteSwayDamping && hasParent && hasPlayBase && playBase._player != null)
                    {
                        float remainSec = (parentLine._judgeTime - playBase._player.msCurTime) / 1000f;
                        if (remainSec < SaveCustomConfig.NoteSwayDampingTime)
                            damp = Clamp01(remainSec / SaveCustomConfig.NoteSwayDampingTime);
                    }

                    lp.x = origX + wave * ampWorld * damp;
                    if (!needChaos)
                        tr.localPosition = lp;
                }

                // 2. NoteSpeedChaos (매 프레임 origY로 먼저 복원 후 배율 적용 → 누적 폭주 방지)
                bool hasWorldPos = false;
                Vector3 wp = default;
                if (needChaos)
                {
                    lp.y = origY;
                    tr.localPosition = lp;

                    float mult = GetChaosMultiplier(note);
                    wp = tr.position;
                    float dy = wp.y - judgeWorldY;
                    wp.y = judgeWorldY + dy * mult;
                    tr.position = wp;
                    hasWorldPos = true;
                }

                // 3. Sudden / Hidden / FadeIn / FadeOut 알파 계산
                if (needCover || needFade || SaveCustomConfig.LaneCoverDebug)
                {
                    var cam = _frameCam;
                    if (cam is not null)
                    {
                        if (!hasWorldPos)
                            wp = tr.position;
                        Vector3 noteScreen = cam.WorldToScreenPoint(wp);
                        int screenH = _frameScreenH;

                        // 판정선 화면 좌표는 같은 프레임·같은 judgeWorldY면 한 번만 투영(노트당 WorldToScreenPoint 2회 → 1회)
                        float judgeGuiY;
                        bool scrollUp;
                        float laneHeightPx;
                        if (_frameJudgeValid && MathF.Abs(judgeWorldY - _frameJudgeWorldY) <= 0.001f)
                        {
                            judgeGuiY = _frameJudgeGuiY;
                            scrollUp = _frameScrollUp;
                            laneHeightPx = _frameLaneH;
                        }
                        else
                        {
                            Vector3 judgeWp = wp;
                            judgeWp.y = judgeWorldY;
                            Vector3 judgeScreen = cam.WorldToScreenPoint(judgeWp);
                            judgeGuiY = screenH - judgeScreen.y;
                            scrollUp = judgeGuiY < screenH * 0.5f;
                            laneHeightPx = scrollUp
                                ? MathF.Max(200f, screenH - judgeGuiY)
                                : MathF.Max(200f, judgeGuiY);
                            _frameJudgeValid = true;
                            _frameJudgeWorldY = judgeWorldY;
                            _frameJudgeGuiY = judgeGuiY;
                            _frameScrollUp = scrollUp;
                            _frameLaneH = laneHeightPx;
                        }

                        // GUI 좌표계(상단=0, 하단=screenH)로 변환
                        float noteGuiY = screenH - noteScreen.y;

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
                                : 1f - Clamp01((u - (suddenEdge - softU * 0.5f)) / softU);
                            alpha = MathF.Min(alpha, aSudden);
                        }

                        // Hidden (판정선쪽 가림: u < Hidden/100)
                        if (SaveCustomConfig.Hidden > 0.01f)
                        {
                            float hiddenEdge = SaveCustomConfig.Hidden / 100f;
                            float aHidden = softU <= 0.0001f
                                ? (u < hiddenEdge ? 0f : 1f)
                                : Clamp01((u - (hiddenEdge - softU * 0.5f)) / softU);
                            alpha = MathF.Min(alpha, aHidden);
                        }

                        // FadeIn (u가 FadeInStart%에서 FadeInStart-FadeInLength%로 내려올 때 0→1)
                        if (SaveCustomConfig.FadeIn)
                        {
                            float startU = SaveCustomConfig.FadeInStart / 100f;
                            float endU = (SaveCustomConfig.FadeInStart - SaveCustomConfig.FadeInLength) / 100f;
                            float aFi = Clamp01((startU - u) / MathF.Max(0.001f, startU - endU));
                            alpha = MathF.Min(alpha, aFi);
                        }

                        // FadeOut (u가 FadeOutStart%에서 FadeOutStart-FadeOutLength%로 내려올 때 1→0)
                        if (SaveCustomConfig.FadeOut)
                        {
                            float startU = SaveCustomConfig.FadeOutStart / 100f;
                            float endU = (SaveCustomConfig.FadeOutStart - SaveCustomConfig.FadeOutLength) / 100f;
                            float aFo = Clamp01((u - endU) / MathF.Max(0.001f, startU - endU));
                            alpha = MathF.Min(alpha, aFo);
                        }

                        if (needCover || needFade)
                        {
                            SetSpriteAlphaIfChanged(note._sp, alpha);
                            SetSpriteAlphaIfChanged(note._additive, alpha);
                            if (longNote is not null && longNote._renderers != null)
                            {
                                var rends = longNote._renderers;
                                for (int i = 0; i < rends.Length; i++)
                                    SetSpriteAlphaIfChanged(rends[i], alpha);
                            }
                        }
                    }
                }
            }
            catch { }
        }
    }
}
