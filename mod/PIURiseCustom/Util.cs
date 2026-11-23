using System;
using System.IO;
using System.Text;
using Il2Cpp;
using Il2CppData;

namespace PIURiseCustom
{
    /// <summary>
    /// 패치 본문에서 공통으로 쓰는 방어 헬퍼.
    /// IL2CPP Harmony 패치에서 Prefix가 예외를 던지면 원본 메서드까지 통째로 건너뛰므로
    /// (트램펄린이 예외를 삼키고 기본값을 반환) 널 가능 경로는 전부 여기를 거친다.
    /// </summary>
    internal static class Util
    {
        public static string SongId(Song song)
        {
            try { return song?.ID; }
            catch { return null; }
        }

        public static string CurrentSongId()
        {
            try { return PlayerData.Instance?.Song?.ID; }
            catch { return null; }
        }

        public static bool IsCurrentSongCustom()
        {
            return CustomPackManager.IsCatalogSongId(CurrentSongId());
        }

        // ===== 텍스트 인코딩 자동 판별 =====
        // UTF-8(BOM 유무) → 실패 시 CP949(EUC-KR) → Shift-JIS → Latin1 순으로 시도.
        // 한국어 info.txt·BMS 헤더가 EUC-KR로 저장돼도 제목/아티스트가 깨지지 않게 한다.
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);
        private static bool _codePagesRegistered;

        public static string[] ReadAllLinesSmart(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            return SplitLines(DecodeSmart(bytes));
        }

        public static string DecodeSmart(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

            try { return StrictUtf8.GetString(bytes); }
            catch (DecoderFallbackException) { }

            foreach (int codePage in new[] { 949, 932 })
            {
                try
                {
                    var enc = GetCodePage(codePage);
                    if (enc != null)
                        return enc.GetString(bytes);
                }
                catch { }
            }

            return Encoding.Latin1.GetString(bytes);
        }

        private static Encoding GetCodePage(int codePage)
        {
            if (!_codePagesRegistered)
            {
                _codePagesRegistered = true;
                try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
            }
            // 엄격 디코더: CP949로 해석 불가능한 바이트가 있으면 다음 후보(Shift-JIS)로 넘어간다
            return Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }

        private static string[] SplitLines(string text)
        {
            return text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        }
    }
}
