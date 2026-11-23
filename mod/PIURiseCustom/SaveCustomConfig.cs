using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MelonLoader.Utils;
using UnityEngine;

namespace PIURiseCustom
{
    /// <summary>
    /// SaveCustomKey/config.txt 설정 파일 로더 (Sixtar Gate: STARTRAIL / DEFLATE 통합 표준 규격).
    /// - 저장하면 다음 곡(MainStage 진입 시) 자동 핫리로드된다.
    /// - 켜기/끄기 값: 1/0, true/false, 켜짐/꺼짐, on/off, yes/no 모두 지원.
    /// - # 또는 // 로 시작하는 줄은 주석.
    /// </summary>
    internal static class SaveCustomConfig
    {
        public static string ConfigDir => Path.Combine(MelonEnvironment.GameRootDirectory, "SaveCustomKey");
        public static string ConfigPath => Path.Combine(ConfigDir, "config.txt");

        private static DateTime _lastWriteUtc = DateTime.MinValue;
        private static bool _loadedOnce;

        // ===== [기본 / 오프라인] =====
        public static bool OfflineMode { get; private set; } = true;
        public static bool CustomEnabled { get; private set; } = true;
        public static float CustomChartOffsetMs { get; private set; } = 0f;

        // ===== [플레이] =====
        public static bool AutoPlay { get; private set; } = false;
        public static bool AllPerfect { get; private set; } = false;
        public static bool BlockSave { get; private set; } = true;
        public static int MaxScore { get; private set; } = 1000000;

        // ===== [판정바] =====
        public static bool EnableJudgmentBar { get; private set; } = true;
        public static bool JudgmentBarVertical { get; private set; } = true;
        public static int JudgmentBarShape { get; private set; } = 0; // 0=사각, 1=알약, 2=삼각/다이아몬드
        public static int JudgmentBarRangeShape { get; private set; } = -1; // -1=따라감
        public static string JudgmentBarSide { get; private set; } = "Center"; // Left / Right / Center

        // ===== [키뷰어] =====
        public static bool EnableKeyViewer { get; private set; } = true;
        public static Color KeyViewerPressedColor { get; private set; } = new Color(0x26 / 255f, 0xBF / 255f, 0xD9 / 255f, 0xD9 / 255f);
        public static Color KeyViewerNormalColor { get; private set; } = new Color(0x14 / 255f, 0x14 / 255f, 0x14 / 255f, 0xA6 / 255f);
        public static Color KeyViewerGatePressedColor { get; private set; } = new Color(0xFF / 255f, 0x40 / 255f, 0x81 / 255f, 0xE6 / 255f);

        // ===== [노트 흔들림] =====
        public static bool NoteSway { get; private set; } = false;
        public static float NoteSwayAmplitude { get; private set; } = 20f;
        public static float NoteSwaySpeed { get; private set; } = 0.8f;
        public static bool NoteSwayDamping { get; private set; } = true;
        public static float NoteSwayDampingTime { get; private set; } = 0.4f;

        // ===== [노트 속도 카오스] =====
        public static bool NoteSpeedChaos { get; private set; } = false;
        public static float NoteSpeedChaosMin { get; private set; } = 0.6f;
        public static float NoteSpeedChaosMax { get; private set; } = 1.8f;
        public static bool NoteSpeedChaosPerLane { get; private set; } = true;

        // ===== [커스텀 노트 스킨] =====
        public static bool CustomNotes { get; private set; } = false;

        // ===== [서든 / 히든] =====
        public static float Sudden { get; private set; } = 0f;
        public static float Hidden { get; private set; } = 0f;
        public static float LaneCoverSoftness { get; private set; } = 24f;

        // ===== [페이드 인 / 페이드 아웃] =====
        public static bool FadeIn { get; private set; } = false;
        public static float FadeInStart { get; private set; } = 100f;
        public static float FadeInLength { get; private set; } = 20f;
        public static bool FadeOut { get; private set; } = false;
        public static float FadeOutStart { get; private set; } = 20f;
        public static float FadeOutLength { get; private set; } = 20f;

        // ===== [커스텀 곡 마라톤] =====
        public static bool CustomMarathon { get; private set; } = false;
        public static float CustomMarathonAutoNext { get; private set; } = 0f;

        // ===== [진단] =====
        public static bool LaneCoverDebug { get; private set; } = false;

        private const string DefaultTemplate =
@"# PIURiseCustom 모드 설정 파일 (SaveCustomKey/config.txt)
# Sixtar Gate: STARTRAIL / DEFLATE 통합 규격과 호환됩니다.
# 저장하면 다음 곡(플레이 씬에 들어갈 때)부터 적용됩니다. 게임을 다시 켤 필요는 없습니다.
# 켜기/끄기 값: 1/0, true/false, 켜짐/꺼짐, on/off 모두 됩니다. # 또는 // 로 시작하는 줄은 주석입니다.

# ===== [기본 / 오프라인] =====
OfflineMode=1
CustomEnabled=1
CustomChartOffsetMs=0

# ===== [플레이] =====
AutoPlay=0
AllPerfect=0
BlockSave=1
MaxScore=1000000

# ===== [판정바] =====
EnableJudgmentBar=1
JudgmentBarVertical=1
JudgmentBarShape=0
JudgmentBarRangeShape=-1
JudgmentBarSide=Center

# ===== [키뷰어] =====
EnableKeyViewer=1
KeyViewerPressedColor=#26BFD9D9
KeyViewerNormalColor=#141414A6
KeyViewerGatePressedColor=#FF4081E6

# ===== [노트 흔들림] =====
NoteSway=0
NoteSwayAmplitude=20
NoteSwaySpeed=0.8
NoteSwayDamping=1
NoteSwayDampingTime=0.4

# ===== [노트 속도 카오스] =====
NoteSpeedChaos=0
NoteSpeedChaosMin=0.6
NoteSpeedChaosMax=1.8
NoteSpeedChaosPerLane=1

# ===== [커스텀 노트 스킨] =====
CustomNotes=0

# ===== [서든 / 히든] =====
Sudden=0
Hidden=0
LaneCoverSoftness=24

# ===== [페이드 인 / 페이드 아웃] =====
FadeIn=0
FadeInStart=100
FadeInLength=20
FadeOut=0
FadeOutStart=20
FadeOutLength=20

# ===== [커스텀 곡 마라톤] =====
CustomMarathon=0
CustomMarathonAutoNext=0

# ===== [진단] =====
LaneCoverDebug=0
";

        public static void InitAndLoad()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                if (!File.Exists(ConfigPath))
                {
                    File.WriteAllText(ConfigPath, DefaultTemplate, new UTF8Encoding(false));
                    Mod.Log?.Msg($"[Config] 기본 설정 파일 생성: {ConfigPath}");
                }
            }
            catch (Exception ex)
            {
                Mod.Log?.Warning($"[Config] 설정 파일 생성 실패: {ex.Message}");
            }

            ReloadIfChanged(force: true);
        }

        public static void ReloadIfChanged(bool force = false)
        {
            if (!File.Exists(ConfigPath))
                return;

            try
            {
                DateTime writeUtc = File.GetLastWriteTimeUtc(ConfigPath);
                if (!force && writeUtc == _lastWriteUtc)
                    return;

                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (string raw in Util.ReadAllLinesSmart(ConfigPath))
                {
                    string line = raw.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith("//"))
                        continue;

                    int sep = line.IndexOf('=');
                    if (sep <= 0) sep = line.IndexOf(':');
                    if (sep <= 0) continue;

                    string k = line.Substring(0, sep).Trim();
                    string val = line.Substring(sep + 1).Trim();
                    // 인라인 주석 제거 (#RRGGBB 색상값의 선두 #은 보호)
                    int commentIdx = val.IndexOf("//", StringComparison.Ordinal);
                    if (commentIdx >= 0) val = val.Substring(0, commentIdx).Trim();

                    if (!string.IsNullOrEmpty(k))
                        map[k] = val;
                }

                // [기본 / 오프라인]
                // OfflineMode·CustomEnabled는 부팅 때만 적용한다. 곡 목록 주입과 서버 스텁(가짜 JWT)이 부팅 시점에 정해지므로
                // 실행 중에 바꾸면 커스텀 곡이 목록에 남은 채 템플릿 우회가 꺼지거나, 오프라인 토큰으로 실제 서버에 접속하게 된다.
                if (map.TryGetValue("OfflineMode", out string v)) OfflineMode = BootOnly("OfflineMode", ParseBool(v, OfflineMode), OfflineMode);
                if (map.TryGetValue("CustomEnabled", out v)) CustomEnabled = BootOnly("CustomEnabled", ParseBool(v, CustomEnabled), CustomEnabled);
                if (map.TryGetValue("CustomChartOffsetMs", out v)) CustomChartOffsetMs = ParseFloat(v, CustomChartOffsetMs);

                // [플레이]
                if (map.TryGetValue("AutoPlay", out v)) AutoPlay = ParseBool(v, AutoPlay);
                if (map.TryGetValue("AllPerfect", out v)) AllPerfect = ParseBool(v, AllPerfect);
                if (map.TryGetValue("BlockSave", out v)) BlockSave = ParseBool(v, BlockSave);
                if (map.TryGetValue("MaxScore", out v)) MaxScore = Math.Max(1, ParseInt(v, MaxScore));

                // [판정바]
                if (map.TryGetValue("EnableJudgmentBar", out v)) EnableJudgmentBar = ParseBool(v, EnableJudgmentBar);
                if (map.TryGetValue("JudgmentBarVertical", out v)) JudgmentBarVertical = ParseBool(v, JudgmentBarVertical);
                if (map.TryGetValue("JudgmentBarShape", out v)) JudgmentBarShape = Mathf.Clamp(ParseInt(v, JudgmentBarShape), 0, 2);
                if (map.TryGetValue("JudgmentBarRangeShape", out v)) JudgmentBarRangeShape = Mathf.Clamp(ParseInt(v, JudgmentBarRangeShape), -1, 2);
                if (map.TryGetValue("JudgmentBarSide", out v) && !string.IsNullOrWhiteSpace(v)) JudgmentBarSide = v.Trim();

                // [키뷰어]
                if (map.TryGetValue("EnableKeyViewer", out v)) EnableKeyViewer = ParseBool(v, EnableKeyViewer);
                if (map.TryGetValue("KeyViewerPressedColor", out v)) KeyViewerPressedColor = ParseColor(v, KeyViewerPressedColor);
                if (map.TryGetValue("KeyViewerNormalColor", out v)) KeyViewerNormalColor = ParseColor(v, KeyViewerNormalColor);
                if (map.TryGetValue("KeyViewerGatePressedColor", out v)) KeyViewerGatePressedColor = ParseColor(v, KeyViewerGatePressedColor);

                // [노트 흔들림]
                if (map.TryGetValue("NoteSway", out v)) NoteSway = ParseBool(v, NoteSway);
                if (map.TryGetValue("NoteSwayAmplitude", out v)) NoteSwayAmplitude = ParseFloat(v, NoteSwayAmplitude);
                if (map.TryGetValue("NoteSwaySpeed", out v)) NoteSwaySpeed = ParseFloat(v, NoteSwaySpeed);
                if (map.TryGetValue("NoteSwayDamping", out v)) NoteSwayDamping = ParseBool(v, NoteSwayDamping);
                if (map.TryGetValue("NoteSwayDampingTime", out v)) NoteSwayDampingTime = Mathf.Max(0.001f, ParseFloat(v, NoteSwayDampingTime));

                // [노트 속도 카오스]
                if (map.TryGetValue("NoteSpeedChaos", out v)) NoteSpeedChaos = ParseBool(v, NoteSpeedChaos);
                if (map.TryGetValue("NoteSpeedChaosMin", out v)) NoteSpeedChaosMin = Mathf.Max(0.1f, ParseFloat(v, NoteSpeedChaosMin));
                if (map.TryGetValue("NoteSpeedChaosMax", out v)) NoteSpeedChaosMax = Mathf.Max(NoteSpeedChaosMin, ParseFloat(v, NoteSpeedChaosMax));
                if (map.TryGetValue("NoteSpeedChaosPerLane", out v)) NoteSpeedChaosPerLane = ParseBool(v, NoteSpeedChaosPerLane);

                // [커스텀 노트 스킨]
                if (map.TryGetValue("CustomNotes", out v)) CustomNotes = ParseBool(v, CustomNotes);

                // [서든 / 히든]
                if (map.TryGetValue("Sudden", out v)) Sudden = Mathf.Clamp(ParseFloat(v, Sudden), 0f, 90f);
                if (map.TryGetValue("Hidden", out v)) Hidden = Mathf.Clamp(ParseFloat(v, Hidden), 0f, 90f);
                if (Sudden + Hidden > 90f)
                    Hidden = Mathf.Max(0f, 90f - Sudden);
                if (map.TryGetValue("LaneCoverSoftness", out v)) LaneCoverSoftness = Mathf.Max(0f, ParseFloat(v, LaneCoverSoftness));

                // [페이드 인 / 페이드 아웃]
                if (map.TryGetValue("FadeIn", out v)) FadeIn = ParseBool(v, FadeIn);
                if (map.TryGetValue("FadeInStart", out v)) FadeInStart = Mathf.Clamp(ParseFloat(v, FadeInStart), 0f, 100f);
                if (map.TryGetValue("FadeInLength", out v)) FadeInLength = Mathf.Clamp(ParseFloat(v, FadeInLength), 0.1f, 100f);
                if (map.TryGetValue("FadeOut", out v)) FadeOut = ParseBool(v, FadeOut);
                if (map.TryGetValue("FadeOutStart", out v)) FadeOutStart = Mathf.Clamp(ParseFloat(v, FadeOutStart), 0f, 100f);
                if (map.TryGetValue("FadeOutLength", out v)) FadeOutLength = Mathf.Clamp(ParseFloat(v, FadeOutLength), 0.1f, 100f);

                // [커스텀 곡 마라톤]
                if (map.TryGetValue("CustomMarathon", out v)) CustomMarathon = ParseBool(v, CustomMarathon);
                if (map.TryGetValue("CustomMarathonAutoNext", out v)) CustomMarathonAutoNext = Mathf.Max(0f, ParseFloat(v, CustomMarathonAutoNext));

                // [진단]
                if (map.TryGetValue("LaneCoverDebug", out v)) LaneCoverDebug = ParseBool(v, LaneCoverDebug);

                // 읽기에 성공한 뒤에만 수정 시각을 기록한다(저장 중 잠긴 파일을 읽다 실패하면 다음 곡에서 다시 읽음)
                _lastWriteUtc = writeUtc;
                _loadedOnce = true;

                // 기존 ModConfig와 동기화
                if (ModConfig.OfflineMode != null) ModConfig.OfflineMode.Value = OfflineMode;
                if (ModConfig.CustomEnabled != null) ModConfig.CustomEnabled.Value = CustomEnabled;
                if (ModConfig.CustomChartOffsetMs != null) ModConfig.CustomChartOffsetMs.Value = CustomChartOffsetMs;

                Mod.Log?.Msg(
                    $"[Config] 로드 완료 — AutoPlay={AutoPlay}, AllPerfect={AllPerfect}, BlockSave={BlockSave}, MaxScore={MaxScore}, " +
                    $"JudgeBar={EnableJudgmentBar}, KeyViewer={EnableKeyViewer}, Sway={NoteSway}, Chaos={NoteSpeedChaos}, " +
                    $"CustomNotes={CustomNotes}, Sudden={Sudden}%, Hidden={Hidden}%, FadeIn={FadeIn}, FadeOut={FadeOut}, Marathon={CustomMarathon}");
            }
            catch (Exception ex)
            {
                Mod.Log?.Warning($"[Config] 설정 파일 읽기 실패: {ex.Message}");
            }
        }

        private static bool BootOnly(string key, bool requested, bool current)
        {
            if (!_loadedOnce || requested == current)
                return requested;
            Mod.Log?.Warning($"[Config] {key}={(requested ? 1 : 0)} 변경은 게임을 다시 시작해야 적용됩니다(현재 {(current ? 1 : 0)} 유지)");
            return current;
        }

        private static bool ParseBool(string raw, bool fallback)
        {
            if (string.IsNullOrWhiteSpace(raw)) return fallback;
            switch (raw.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "on":
                case "yes":
                case "켜짐":
                case "켜기":
                case "사용":
                    return true;
                case "0":
                case "false":
                case "off":
                case "no":
                case "꺼짐":
                case "끄기":
                case "사용안함":
                    return false;
                default:
                    return fallback;
            }
        }

        private static int ParseInt(string raw, int fallback)
        {
            return int.TryParse(raw?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int val)
                ? val
                : fallback;
        }

        private static float ParseFloat(string raw, float fallback)
        {
            return float.TryParse(raw?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float val)
                ? val
                : fallback;
        }

        private static Color ParseColor(string raw, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return fallback;

            string s = raw.Trim();

            // 한글/영문 색 이름
            switch (s.ToLowerInvariant())
            {
                case "시안": case "cyan": return new Color(0f, 1f, 1f, 0.85f);
                case "마젠타": case "magenta": return new Color(1f, 0f, 1f, 0.85f);
                case "노랑": case "yellow": return new Color(1f, 0.92f, 0.016f, 0.85f);
                case "빨강": case "red": return new Color(1f, 0.2f, 0.2f, 0.85f);
                case "파랑": case "blue": return new Color(0.2f, 0.45f, 1f, 0.85f);
                case "초록": case "green": return new Color(0.2f, 0.9f, 0.35f, 0.85f);
                case "흰색": case "white": return new Color(1f, 1f, 1f, 0.9f);
                case "검정": case "black": return new Color(0.08f, 0.08f, 0.08f, 0.85f);
                case "주황": case "orange": return new Color(1f, 0.55f, 0.1f, 0.85f);
                case "보라": case "purple": return new Color(0.65f, 0.25f, 1f, 0.85f);
                case "분홍": case "pink": return new Color(1f, 0.4f, 0.7f, 0.85f);
                case "하늘": case "sky": return new Color(0.35f, 0.8f, 1f, 0.85f);
                case "민트": case "mint": return new Color(0.25f, 0.95f, 0.75f, 0.85f);
            }

            // #RRGGBB 또는 #RRGGBBAA
            if (s.StartsWith("#"))
            {
                string hex = s.Substring(1);
                if (hex.Length == 6 &&
                    byte.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r6) &&
                    byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g6) &&
                    byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b6))
                {
                    return new Color(r6 / 255f, g6 / 255f, b6 / 255f, 1f);
                }
                if (hex.Length == 8 &&
                    byte.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r8) &&
                    byte.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g8) &&
                    byte.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b8) &&
                    byte.TryParse(hex.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte a8))
                {
                    return new Color(r8 / 255f, g8 / 255f, b8 / 255f, a8 / 255f);
                }
            }

            // R,G,B 또는 R,G,B,A (0..255 또는 0..1)
            string[] parts = s.Split(',');
            if (parts.Length == 3 || parts.Length == 4)
            {
                if (float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float cr) &&
                    float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float cg) &&
                    float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float cb))
                {
                    float ca = parts.Length == 4 && float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedA)
                        ? parsedA
                        : (cr > 1f || cg > 1f || cb > 1f ? 255f : 1f);

                    if (cr > 1f || cg > 1f || cb > 1f || ca > 1f)
                        return new Color(cr / 255f, cg / 255f, cb / 255f, ca / 255f);
                    return new Color(cr, cg, cb, ca);
                }
            }

            return fallback;
        }
    }
}
