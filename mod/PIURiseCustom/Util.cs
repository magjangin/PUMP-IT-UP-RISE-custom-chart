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
        // 차트는 스레드 풀(CustomChartParsers.Prefetch)에서도 디코딩한다. 예전 "bool 플래그 → 등록" 방식은 한 스레드가 플래그만 세우고
        // 등록하기 전에 다른 스레드가 CP949를 찾으면 실패해 Latin1로 떨어져(제목·키음 이름 깨짐) 경쟁이 있었다 → 한 번만, 잠금으로 등록(v0.3.14).
        private static readonly Lazy<bool> _codePagesRegistered = new(() =>
        {
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return true; }
            catch { return false; }
        }, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        private static readonly Lazy<Encoding> _cp949 = new(() =>
        {
            _ = _codePagesRegistered.Value;
            return Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        private static readonly Lazy<Encoding> _cp932 = new(() =>
        {
            _ = _codePagesRegistered.Value;
            return Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        public static string[] ReadAllLinesSmart(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            return SplitLines(DecodeSmart(bytes));
        }

        /// <summary>
        /// 큰 차트 파일의 앞부분 헤더만 빠르게 읽는다(부팅 스캔 시 전체 파일 읽기·디코딩 회피).
        /// UTF-8 멀티바이트 경계가 maxBytes에서 잘릴 수 있으므로, 잘린 끝 1~3바이트를 보정해 디코딩한다.
        /// </summary>
        public static string[] ReadFirstLinesSmart(string path, int maxLines = 200, int maxBytes = 32768)
        {
            using var fs = File.OpenRead(path);
            int toRead = (int)Math.Min(fs.Length, maxBytes);
            byte[] buf = new byte[toRead];
            int read = 0;
            while (read < toRead)
            {
                int n = fs.Read(buf, read, toRead - read);
                if (n <= 0) break;
                read += n;
            }
            if (read != buf.Length)
                Array.Resize(ref buf, read);

            // 파일 끝이 아닌 중간에서 잘렸다면 마지막 개행 직전까지만 취해 멀티바이트 문자 절단을 막는다
            if (fs.Length > read && read > 0)
            {
                int lastNl = Array.LastIndexOf(buf, (byte)'\n');
                if (lastNl > 0)
                    Array.Resize(ref buf, lastNl);
            }

            string[] lines = SplitLines(DecodeSmart(buf));
            if (lines.Length <= maxLines)
                return lines;
            var trimmed = new string[maxLines];
            Array.Copy(lines, trimmed, maxLines);
            return trimmed;
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
            if (codePage == 949) return _cp949.Value;
            if (codePage == 932) return _cp932.Value;
            _ = _codePagesRegistered.Value;
            // 엄격 디코더: CP949로 해석 불가능한 바이트가 있으면 다음 후보(Shift-JIS)로 넘어간다
            return Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }

        private static string[] SplitLines(string text)
        {
            return text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        }
    }
}
