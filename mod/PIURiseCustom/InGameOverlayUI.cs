using System;
using Il2Cpp;
using Il2CppPIUMobileStepDLL;
using UnityEngine;
using UnityEngine.UI;

namespace PIURiseCustom
{
    /// <summary>
    /// 순수 uGUI(Canvas + Image) 기반 실시간 오버레이 (IL2CPP에서 스트립된 OnGUI 미사용):
    /// - 판정바 (EnableJudgmentBar, Vertical/Horizontal, Shape 0/1/2, RangeShape, Side)
    /// - 키뷰어 (EnableKeyViewer, Pressed/Normal/GatePressed 색상)
    /// - 서든/히든/페이드 진단선 (LaneCoverDebug)
    /// </summary>
    internal static class InGameOverlayUI
    {
        private const int MaxTicks = 24;

        private struct TickData
        {
            public Image Img;
            public Color BaseColor; // 생성/갱신 시 색을 기억 → 매 프레임 페이드에서 img.color getter(IL2CPP 호출)를 읽지 않는다
            public float Time;
            public bool Active; // SetActive 상태를 관리 쪽에 기억 → 매 프레임 틱마다 gameObject.activeSelf(IL2CPP 호출 2회)를 읽지 않는다
        }

        private static GameObject _canvasObj;
        private static Sprite _sprWhite;
        private static Sprite _sprCapsule;
        private static Sprite _sprDiamond;

        // 판정바 UI
        private static GameObject _judgeRoot;
        private static readonly TickData[] _ticks = new TickData[MaxTicks];
        private static int _tickHead = 0;
        private static float _perfectWindowMs = 41.6f;
        private static float _greatWindowMs = 83.3f;
        private static float _goodWindowMs = 125f;
        private static float _maxWindowMs = 166.6f;
        private static bool _barVertical = true;
        private static float _barW = 22f;
        private static float _barH = 260f;

        // 키뷰어 UI
        private static GameObject _kvRoot;
        private static readonly Image[] _kvBoxes = new Image[5];
        // 레인별 마지막 표시 상태 (-1 = 아직 칠하지 않음). 상태가 바뀔 때만 Image.color를 써서 매 프레임 interop 호출·UI 재빌드를 줄인다.
        private static readonly int[] _kvLastState = { -1, -1, -1, -1, -1 };
        // 스테이지 동안 키 테이블 4종 래퍼를 재사용(매 프레임 Il2CppStructArray 래퍼 4개 할당 + IL2CPP 필드 getter 4회 제거)
        private static IntPtr _cachedKeyGimPtr;
        private static KeyTables _cachedKeyTables;

        // LaneCoverDebug UI
        private static GameObject _dbgRoot;
        private static RectTransform _dbgJudge;
        private static RectTransform _dbgSudden;
        private static RectTransform _dbgHidden;

        internal static bool IsInStage = false;
        private static bool _loggedError = false;

        public static void ResetForNewStage()
        {
            _tickHead = 0;
            IsInStage = true;
            DestroyCanvas();
        }

        public static void ClearStage()
        {
            IsInStage = false;
            DestroyCanvas();
        }

        private static void DestroyCanvas()
        {
            try
            {
                if (_canvasObj != null)
                    UnityEngine.Object.Destroy(_canvasObj);
            }
            catch { }
            _canvasObj = null;
            _judgeRoot = null;
            _kvRoot = null;
            _dbgRoot = null;
            for (int i = 0; i < MaxTicks; i++) _ticks[i] = default;
            for (int i = 0; i < _kvBoxes.Length; i++) _kvBoxes[i] = null;
            for (int i = 0; i < _kvLastState.Length; i++) _kvLastState[i] = -1;
            _cachedKeyGimPtr = IntPtr.Zero;
            _cachedKeyTables = default;
        }

        public static void RecordJudgment(float deltaMs, int grade, JudgeTiming timing)
        {
            if (timing != null && timing.Perfect > 1f)
            {
                _perfectWindowMs = timing.Perfect;
                float iv = timing.Interval > 1f ? timing.Interval : 41.6f;
                _greatWindowMs = _perfectWindowMs + iv;
                _goodWindowMs = _greatWindowMs + iv;
                _maxWindowMs = _goodWindowMs + iv;
            }

            try
            {
                if (_judgeRoot is null) return; // 판정마다 불리는 경로: 관리 비교
                float maxMs = MathF.Max(120f, _maxWindowMs);
                float norm = Math.Clamp(deltaMs / maxMs, -1f, 1f);

                int slot = _tickHead;
                _tickHead = (_tickHead + 1) % MaxTicks;
                var t = _ticks[slot];
                if (t.Img is not null)
                {
                    Color col = grade switch
                    {
                        0 => new Color(0.3f, 0.95f, 1f, 0.95f),
                        1 => new Color(0.35f, 1f, 0.45f, 0.95f),
                        2 => new Color(1f, 0.9f, 0.25f, 0.95f),
                        _ => new Color(1f, 0.35f, 0.25f, 0.95f)
                    };
                    t.Img.color = col;
                    var rt = t.Img.rectTransform;
                    if (_barVertical)
                        rt.anchoredPosition = new Vector2(0f, -norm * (_barH * 0.48f));
                    else
                        rt.anchoredPosition = new Vector2(norm * (_barW * 0.48f), 0f);
                    if (!t.Active)
                        t.Img.gameObject.SetActive(true);
                    _ticks[slot].BaseColor = col;
                    _ticks[slot].Time = Time.unscaledTime;
                    _ticks[slot].Active = true;
                }
            }
            catch { }
        }

        private static void EnsureSprites()
        {
            if (_sprWhite == null)
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                var cols = new Color32[] { new(255, 255, 255, 255), new(255, 255, 255, 255), new(255, 255, 255, 255), new(255, 255, 255, 255) };
                tex.SetPixels32(cols);
                tex.Apply();
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                _sprWhite = Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 100f);
                _sprWhite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            if (_sprCapsule == null)
            {
                _sprCapsule = CreateMaskSprite(64, (dx, dy) => (0.5f - MathF.Sqrt(dx * dx + dy * dy)) * 24f);
            }
            if (_sprDiamond == null)
            {
                _sprDiamond = CreateMaskSprite(64, (dx, dy) => (0.5f - (MathF.Abs(dx) + MathF.Abs(dy))) * 24f);
            }
        }

        /// <summary>
        /// 흰색 알파 마스크 스프라이트를 만든다. 픽셀 계산은 관리 배열에서 끝내고 SetPixels32 한 번으로 올린다
        /// (예전: 64×64 픽셀마다 Mathf 3회 + SetPixel 1회 = 모양당 약 1.6만 번의 IL2CPP 호출이 첫 곡 시작 때 몰렸다, v0.3.4).
        /// alpha(dx, dy)의 dx·dy는 중심 기준 -0.5..0.5, 반환값은 0..1로 잘린다.
        /// </summary>
        private static Sprite CreateMaskSprite(int size, Func<float, float, float> alpha)
        {
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float dy = (y + 0.5f) / size - 0.5f;
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size - 0.5f;
                    float a = Math.Clamp(alpha(dx, dy), 0f, 1f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f + 0.5f));
                }
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var sp = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sp.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sp;
        }

        private static Sprite GetShapeSprite(int shape) => shape switch
        {
            1 => _sprCapsule,
            2 => _sprDiamond,
            _ => _sprWhite
        };

        private static Image CreateImage(string name, Transform parent, Vector2 size, Vector2 pos, Color col, int shape = 0)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = GetShapeSprite(shape);
            img.color = col;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return img;
        }

        private static void BuildCanvasIfNeeded()
        {
            // _canvasObj는 우리가 만들고 우리가 null로 비운다(씬 전환·스테이지 종료 때 ClearStage). 매 프레임 Unity 비교 대신 관리 비교.
            if (_canvasObj is not null) return;
            if (!SaveCustomConfig.EnableJudgmentBar && !SaveCustomConfig.EnableKeyViewer && !SaveCustomConfig.LaneCoverDebug)
                return;

            EnsureSprites();

            _canvasObj = new GameObject("PIURiseCustom_OverlayCanvas");
            var canvas = _canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;

            var scaler = _canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            // 1. 판정바 생성
            if (SaveCustomConfig.EnableJudgmentBar)
            {
                _barVertical = SaveCustomConfig.JudgmentBarVertical;
                _barW = _barVertical ? 24f : 320f;
                _barH = _barVertical ? 320f : 24f;
                int barShape = SaveCustomConfig.JudgmentBarShape;
                int rangeShape = SaveCustomConfig.JudgmentBarRangeShape < 0 ? barShape : SaveCustomConfig.JudgmentBarRangeShape;
                string side = (SaveCustomConfig.JudgmentBarSide ?? "Center").Trim().ToLowerInvariant();

                _judgeRoot = new GameObject("JudgmentBar");
                _judgeRoot.transform.SetParent(_canvasObj.transform, false);
                var jrt = _judgeRoot.AddComponent<RectTransform>();
                jrt.sizeDelta = new Vector2(_barW, _barH);

                if (_barVertical)
                {
                    float x = side == "right" ? 880f : -880f;
                    jrt.anchoredPosition = new Vector2(x, 0f);
                }
                else
                {
                    float x = side == "left" ? -720f : (side == "right" ? 720f : 0f);
                    jrt.anchoredPosition = new Vector2(x, -260f);
                }

                // 배경
                CreateImage("BG", _judgeRoot.transform, new Vector2(_barW + 8f, _barH + 8f), Vector2.zero, new Color(0.05f, 0.05f, 0.08f, 0.72f), barShape);

                // 구간 밴드
                float maxMs = MathF.Max(120f, _maxWindowMs);
                CreateBand("Bad", 1f, new Color(1f, 0.35f, 0.2f, 0.28f), rangeShape);
                CreateBand("Good", _goodWindowMs / maxMs, new Color(1f, 0.85f, 0.2f, 0.35f), rangeShape);
                CreateBand("Great", _greatWindowMs / maxMs, new Color(0.25f, 0.95f, 0.45f, 0.42f), rangeShape);
                CreateBand("Perfect", _perfectWindowMs / maxMs, new Color(0.2f, 0.85f, 1f, 0.55f), rangeShape);

                // 중앙선 (흰색 기준선 1개만 유지)
                Vector2 centerSize = _barVertical ? new Vector2(_barW + 4f, 2f) : new Vector2(2f, _barH + 4f);
                CreateImage("Center", _judgeRoot.transform, centerSize, Vector2.zero, Color.white);

                // 틱 풀
                Vector2 tickSize = _barVertical ? new Vector2(_barW - 2f, 3f) : new Vector2(3f, _barH - 2f);
                for (int i = 0; i < MaxTicks; i++)
                {
                    var img = CreateImage($"Tick_{i}", _judgeRoot.transform, tickSize, Vector2.zero, Color.clear);
                    img.gameObject.SetActive(false);
                    _ticks[i] = new TickData { Img = img, Time = -10f, Active = false };
                }
            }

            // 2. 키뷰어 생성 (우측 하단 5키)
            if (SaveCustomConfig.EnableKeyViewer)
            {
                _kvRoot = new GameObject("KeyViewer");
                _kvRoot.transform.SetParent(_canvasObj.transform, false);
                var krt = _kvRoot.AddComponent<RectTransform>();
                krt.anchorMin = new Vector2(1f, 0f);
                krt.anchorMax = new Vector2(1f, 0f);
                krt.pivot = new Vector2(1f, 0f);
                krt.anchoredPosition = new Vector2(-36f, 36f);
                krt.sizeDelta = new Vector2(300f, 64f);

                const float boxW = 52f;
                const float boxH = 56f;
                const float gap = 8f;
                for (int i = 0; i < 5; i++)
                {
                    float x = -((4 - i) * (boxW + gap)) - boxW * 0.5f;
                    var border = CreateImage($"KeyBorder_{i}", _kvRoot.transform, new Vector2(boxW + 4f, boxH + 4f), new Vector2(x, boxH * 0.5f), new Color(1f, 1f, 1f, 0.4f));
                    border.rectTransform.anchorMin = new Vector2(1f, 0f);
                    border.rectTransform.anchorMax = new Vector2(1f, 0f);

                    var box = CreateImage($"Key_{i}", border.transform, new Vector2(boxW, boxH), Vector2.zero, SaveCustomConfig.KeyViewerNormalColor);
                    _kvBoxes[i] = box;
                }
            }

            // 3. LaneCoverDebug 선
            if (SaveCustomConfig.LaneCoverDebug)
            {
                _dbgRoot = new GameObject("LaneCoverDebug");
                _dbgRoot.transform.SetParent(_canvasObj.transform, false);
                _dbgJudge = CreateImage("DbgJudge", _dbgRoot.transform, new Vector2(1920f, 3f), Vector2.zero, Color.red).rectTransform;
                _dbgSudden = CreateImage("DbgSudden", _dbgRoot.transform, new Vector2(1920f, 3f), Vector2.zero, Color.cyan).rectTransform;
                _dbgHidden = CreateImage("DbgHidden", _dbgRoot.transform, new Vector2(1920f, 3f), Vector2.zero, Color.green).rectTransform;
            }
        }

        private static void CreateBand(string name, float ratio, Color col, int shape)
        {
            ratio = Math.Clamp(ratio, 0f, 1f);
            Vector2 sz = _barVertical ? new Vector2(_barW, _barH * ratio) : new Vector2(_barW * ratio, _barH);
            CreateImage(name, _judgeRoot.transform, sz, Vector2.zero, col, shape);
        }

        public static void UpdateOverlay()
        {
            if (!IsInStage)
                return;

            // 오버레이 기능이 전부 꺼져 있고 만든 캔버스도 없으면 매 프레임 MainStage/StageController 조회(IL2CPP 호출)조차 하지 않는다
            if (_canvasObj is null && !SaveCustomConfig.EnableJudgmentBar && !SaveCustomConfig.EnableKeyViewer && !SaveCustomConfig.LaneCoverDebug)
                return;

            // 일시정지 나가기 등으로 MainStage가 비활성/파괴된 경우 선곡창에 오버레이가 남지 않도록 즉시 정리
            var stage = MainStage.Instance;
            if (stage == null || !stage.isActiveAndEnabled)
            {
                ClearStage();
                return;
            }

            // 로딩 커튼이 내려가고 실제 곡이 시작된 뒤에만 판정바/키뷰어 표시
            var sc = StageController.Instance;
            if (sc == null || !sc.GameStarted)
                return;

            try
            {
                BuildCanvasIfNeeded();
                if (_canvasObj is null) return;

                // 판정바 틱 페이드 갱신
                if (_judgeRoot is not null)
                {
                    float now = Time.unscaledTime;
                    for (int i = 0; i < MaxTicks; i++)
                    {
                        if (!_ticks[i].Active) continue;
                        var img = _ticks[i].Img;
                        if (img is null) continue;
                        float age = now - _ticks[i].Time;
                        if (age > 2.5f)
                        {
                            img.gameObject.SetActive(false);
                            _ticks[i].Active = false;
                        }
                        else
                        {
                            Color c = _ticks[i].BaseColor;
                            float a = 1f - age / 2.5f;
                            c.a = a < 0f ? 0f : a;
                            img.color = c;
                        }
                    }
                }

                // 키뷰어 갱신 (실제 인게임 입력 시스템 GameInputManager 기반)
                if (_kvRoot is not null)
                {
                    var gim = GameInputManager.Instance;
                    if (gim != null)
                    {
                        IntPtr gimPtr = gim.Pointer;
                        if (gimPtr != _cachedKeyGimPtr)
                        {
                            _cachedKeyGimPtr = gimPtr;
                            _cachedKeyTables = KeyTables.Fetch(gim);
                        }
                        var tables = _cachedKeyTables;
                        for (int i = 0; i < 5; i++)
                        {
                            var box = _kvBoxes[i];
                            if (box is null) continue;
                            int state = IsLanePressed(gim, tables, i) ? 1 : 0;
                            if (state == _kvLastState[i]) continue;
                            _kvLastState[i] = state;
                            bool isCenter = i == 2;
                            box.color = state == 1
                                ? (isCenter ? SaveCustomConfig.KeyViewerGatePressedColor : SaveCustomConfig.KeyViewerPressedColor)
                                : SaveCustomConfig.KeyViewerNormalColor;
                        }
                    }
                }

                // LaneCoverDebug 진단선 갱신 (빨강=판정선, 하늘=서든/가림 경계, 연두=히든/페이드 끝)
                if (_dbgRoot is not null)
                {
                    float judgeGuiY = NoteVisualPatches.LastJudgeScreenY;
                    float laneH = NoteVisualPatches.LastLaneScreenHeight;
                    bool scrollUp = NoteVisualPatches.LastScrollUp;

                    if (judgeGuiY < 0f && Camera.main != null && GameplayOptionPatches.ActivePlayBase != null)
                    {
                        float judgeWorldY = GameplayOptionPatches.ActivePlayBase.ARROW_Y;
                        Vector3 js = Camera.main.WorldToScreenPoint(new Vector3(0f, judgeWorldY, 0f));
                        judgeGuiY = Screen.height - js.y;
                        scrollUp = judgeGuiY < Screen.height * 0.5f;
                        laneH = scrollUp
                            ? MathF.Max(200f, Screen.height - judgeGuiY)
                            : MathF.Max(200f, judgeGuiY);
                    }

                    if (judgeGuiY >= 0f && Screen.height > 0)
                    {
                        float UToCanvasY(float u)
                        {
                            float guiY = scrollUp ? (judgeGuiY + u * laneH) : (judgeGuiY - u * laneH);
                            return 540f - (guiY / Screen.height) * 1080f;
                        }

                        if (_dbgJudge != null)
                        {
                            _dbgJudge.gameObject.SetActive(true);
                            _dbgJudge.anchoredPosition = new Vector2(0f, UToCanvasY(0f));
                        }

                        if (_dbgSudden != null)
                        {
                            bool showSudden = SaveCustomConfig.Sudden > 0.01f || SaveCustomConfig.FadeIn;
                            _dbgSudden.gameObject.SetActive(showSudden);
                            if (showSudden)
                            {
                                float u = SaveCustomConfig.Sudden > 0.01f
                                    ? 1f - (SaveCustomConfig.Sudden / 100f)
                                    : (SaveCustomConfig.FadeInStart - SaveCustomConfig.FadeInLength) / 100f;
                                _dbgSudden.anchoredPosition = new Vector2(0f, UToCanvasY(u));
                            }
                        }

                        if (_dbgHidden != null)
                        {
                            bool showHidden = SaveCustomConfig.Hidden > 0.01f || SaveCustomConfig.FadeOut;
                            _dbgHidden.gameObject.SetActive(showHidden);
                            if (showHidden)
                            {
                                float u = SaveCustomConfig.Hidden > 0.01f
                                    ? SaveCustomConfig.Hidden / 100f
                                    : (SaveCustomConfig.FadeOutStart - SaveCustomConfig.FadeOutLength) / 100f;
                                _dbgHidden.anchoredPosition = new Vector2(0f, UToCanvasY(u));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_loggedError)
                {
                    _loggedError = true;
                    Mod.Log?.Warning($"[OverlayUI] uGUI 갱신 오류: {ex}");
                }
            }
        }

        /// <summary>
        /// 키 테이블 4종을 프레임당 한 번만 읽는다. interop 필드 getter는 읽을 때마다 새 래퍼 객체를 만들어서
        /// 예전처럼 레인마다 다시 읽으면 프레임당 최대 20개의 래퍼가 생겼다.
        /// </summary>
        private struct KeyTables
        {
            public Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<GameInputManager.BTN_MEMBER> K5, K5Sub, K6, K6Sub;

            public static KeyTables Fetch(GameInputManager gim)
            {
                if (gim == null) return default;
                return new KeyTables { K5 = gim.keyTbl5k, K5Sub = gim.keyTbl5kSub, K6 = gim.keyTbl6k, K6Sub = gim.keyTbl6kSub };
            }
        }

        private static bool IsBtnDown(GameInputManager gim, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<GameInputManager.BTN_MEMBER> tbl, int idx)
        {
            if (gim == null || tbl == null || idx < 0 || idx >= tbl.Length)
                return false;
            var st = gim.GetButtonState(tbl[idx]);
            return st == GameInputManager.INPUT_STATE.PRESS || st == GameInputManager.INPUT_STATE.HOLD;
        }

        private static bool IsLanePressed(GameInputManager gim, KeyTables t, int lane)
        {
            if (gim == null)
                return false;

            if (IsBtnDown(gim, t.K5, lane) || IsBtnDown(gim, t.K5Sub, lane))
                return true;

            // 6K 모드(중앙키 2개 포함 6버튼)도 함께 반영
            var k6 = t.K6;
            var k6s = t.K6Sub;
            if (k6 != null && k6.Length >= 6)
            {
                if (lane == 2)
                {
                    if (IsBtnDown(gim, k6, 2) || IsBtnDown(gim, k6, 3) || IsBtnDown(gim, k6s, 2) || IsBtnDown(gim, k6s, 3))
                        return true;
                }
                else
                {
                    int mapped = lane < 2 ? lane : lane + 1;
                    if (IsBtnDown(gim, k6, mapped) || IsBtnDown(gim, k6s, mapped))
                        return true;
                }
            }
            else if (IsBtnDown(gim, k6, lane) || IsBtnDown(gim, k6s, lane))
            {
                return true;
            }

            return false;
        }
    }
}
