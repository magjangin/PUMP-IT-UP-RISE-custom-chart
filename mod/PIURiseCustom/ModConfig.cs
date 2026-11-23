using System.IO;
using MelonLoader;
using MelonLoader.Utils;

namespace PIURiseCustom
{
    /// <summary>
    /// MelonPreferences(UserData/MelonPreferences.cfg) 항목.
    /// OfflineMode / CustomEnabled / CustomChartOffsetMs는 SaveCustomKey/config.txt가 원본이며,
    /// config.txt를 읽을 때마다 이 값들을 덮어쓴다(SaveCustomConfig.ReloadIfChanged). cfg에서 바꿔도 config.txt 값이 이긴다.
    /// </summary>
    internal static class ModConfig
    {
        private static MelonPreferences_Category _cat;

        public static MelonPreferences_Entry<bool> OfflineMode;
        public static MelonPreferences_Entry<bool> CustomEnabled;
        public static MelonPreferences_Entry<bool> CaptureOnlineResponses;
        public static MelonPreferences_Entry<bool> OverridePreviewAudio;
        public static MelonPreferences_Entry<float> CustomChartOffsetMs;
        public static MelonPreferences_Entry<string> CustomRootFolder;

        public static string ResolvedCustomRoot
        {
            get
            {
                string val = CustomRootFolder?.Value;
                if (string.IsNullOrWhiteSpace(val))
                    return Path.Combine(MelonEnvironment.GameRootDirectory, "hwa");
                return Path.IsPathRooted(val)
                    ? val
                    : Path.Combine(MelonEnvironment.GameRootDirectory, val);
            }
        }

        public static void Init()
        {
            _cat = MelonPreferences.CreateCategory("PIURiseCustom", "PIU RISE Custom & Offline");

            OfflineMode = _cat.CreateEntry("OfflineMode", true,
                "오프라인 서버 스텁 활성화",
                "true: 게임 서버 통신을 전부 차단하고 로컬 스텁 응답을 반환합니다(커스텀 플레이 시 온라인 기록 오염 방지).");

            CustomEnabled = _cat.CreateEntry("CustomEnabled", true,
                "커스텀 차트/BGM/BGA 주입 활성화",
                "SaveCustomKey/config.txt의 CustomEnabled 값이 우선합니다. hwa/ 폴더 변경은 게임 재시작 시 반영됩니다.");

            CaptureOnlineResponses = _cat.CreateEntry("CaptureOnlineResponses", false,
                "온라인 응답 JSON 캡처 (OfflineMode=false 일 때만 동작)",
                "true로 켜고 1회 정상 접속하면 MelonLoader/net_capture.jsonl에 서버 응답 형식을 기록합니다.");

            OverridePreviewAudio = _cat.CreateEntry("OverridePreviewAudio", true,
                "선곡 미리듣기 BGM도 커스텀으로 교체");

            CustomChartOffsetMs = _cat.CreateEntry("CustomChartOffsetMs", 0f,
                "커스텀 차트 공통 싱크 오프셋 (ms)");

            CustomRootFolder = _cat.CreateEntry("CustomRootFolder", "hwa",
                "커스텀 곡 루트 폴더 (게임 폴더 기준 상대경로 또는 절대경로, 기본값: hwa)");
        }
    }
}
