using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace PIURiseCustom
{
    /// <summary>디코딩한 이미지: RGBA32, 아래 행부터(Unity Texture2D.LoadRawTextureData 순서).</summary>
    internal sealed class DecodedImage
    {
        public int Width;
        public int Height;
        public byte[] Rgba;
    }

    /// <summary>
    /// 순수 관리 코드 PNG 디코더 (v0.3.12). Unity·Il2Cpp를 전혀 쓰지 않아 백그라운드 스레드에서 돌릴 수 있다.
    /// 자켓·로딩 썸네일을 ImageConversion.LoadImage(메인 스레드 동기 디코딩)로 풀면 큰 PNG에서 수십 ms씩 프레임이 멈췄다.
    /// 여기서 미리 RGBA로 풀어 두면 메인 스레드는 LoadRawTextureData(복사)만 한다.
    ///
    /// 지원: 비인터레이스, 비트 깊이 1/2/4/8/16, 색 형식 0(회색)/2(RGB)/3(팔레트)/4(회색+알파)/6(RGBA), tRNS.
    /// 그 밖(인터레이스, 손상 파일, 너무 큰 이미지 등)은 null을 돌려준다 → 호출자가 원래 LoadImage 경로로 폴백한다.
    /// 감마·ICC 보정은 하지 않는다(Unity LoadImage와 같게 픽셀 값을 그대로 쓴다 — 표준이 아닌 gAMA가 있으면 폴백).
    /// </summary>
    internal static class PngDecoder
    {
        private const int MaxDimension = 16384;          // Unity 최대 텍스처 크기
        private const long MaxPixels = 64L * 1024 * 1024; // RGBA 256MB 상한

        public static bool IsPng(byte[] data) =>
            data != null && data.Length >= 8 &&
            data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
            data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;

        public static DecodedImage TryDecode(byte[] data)
        {
            try { return Decode(data); }
            catch { return null; }
        }

        private static DecodedImage Decode(byte[] data)
        {
            if (!IsPng(data))
                return null;

            int width = 0, height = 0, bitDepth = 0, colorType = -1;
            byte[] palette = null;   // RGB 트리플
            byte[] paletteAlpha = null;
            int keyR = -1, keyG = -1, keyB = -1; // tRNS 색 키(색 형식 0/2), 원래 비트 깊이 기준 값
            var idat = new MemoryStream();

            int pos = 8;
            bool sawIhdr = false, sawEnd = false;
            while (pos + 8 <= data.Length)
            {
                int len = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos));
                if (len < 0 || pos + 12L + len > data.Length) return null;
                uint type = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 4));
                int body = pos + 8;

                switch (type)
                {
                    case 0x49484452: // IHDR
                        if (len < 13) return null;
                        width = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(body));
                        height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(body + 4));
                        bitDepth = data[body + 8];
                        colorType = data[body + 9];
                        if (data[body + 10] != 0 || data[body + 11] != 0) return null; // 압축·필터 방식
                        if (data[body + 12] != 0) return null;                          // 인터레이스(Adam7) 미지원 → 폴백
                        sawIhdr = true;
                        break;
                    case 0x504C5445: // PLTE
                        palette = data.AsSpan(body, len).ToArray();
                        break;
                    case 0x74524E53: // tRNS
                        if (colorType == 3)
                            paletteAlpha = data.AsSpan(body, len).ToArray();
                        else if (colorType == 0 && len >= 2)
                            keyR = keyG = keyB = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(body));
                        else if (colorType == 2 && len >= 6)
                        {
                            keyR = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(body));
                            keyG = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(body + 2));
                            keyB = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(body + 4));
                        }
                        break;
                    case 0x67414D41: // gAMA: 표준(1/2.2 = 45455) 근처가 아니면 Unity와 색이 달라질 수 있어 폴백
                        if (len >= 4)
                        {
                            uint g = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(body));
                            if (g < 45000 || g > 46000) return null;
                        }
                        break;
                    case 0x49444154: // IDAT
                        idat.Write(data, body, len);
                        break;
                    case 0x49454E44: // IEND
                        sawEnd = true;
                        break;
                }
                if (sawEnd) break;
                pos = body + len + 4; // CRC는 검사하지 않는다(깨진 데이터는 inflate·길이 검사에서 걸린다)
            }

            if (!sawIhdr || width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension)
                return null;
            if ((long)width * height > MaxPixels)
                return null;

            int channels;
            switch (colorType)
            {
                case 0: channels = 1; break;
                case 2: channels = 3; break;
                case 3: channels = 1; if (palette == null) return null; break;
                case 4: channels = 2; break;
                case 6: channels = 4; break;
                default: return null;
            }
            bool depthOk = colorType switch
            {
                0 => bitDepth is 1 or 2 or 4 or 8 or 16,
                3 => bitDepth is 1 or 2 or 4 or 8,
                _ => bitDepth is 8 or 16
            };
            if (!depthOk)
                return null;

            int bitsPerPixel = channels * bitDepth;
            int bpp = Math.Max(1, bitsPerPixel / 8);                // 필터 계산용 바이트 간격
            long strideL = ((long)width * bitsPerPixel + 7) / 8;
            if (strideL > int.MaxValue / 2) return null;
            int stride = (int)strideL;

            // 압축 해제: 행마다 필터 바이트 1 + stride
            long rawLen = (long)(stride + 1) * height;
            if (rawLen > int.MaxValue) return null;
            byte[] raw = new byte[rawLen];
            idat.Position = 0;
            using (var z = new ZLibStream(idat, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < raw.Length)
                {
                    int n = z.Read(raw, read, raw.Length - read);
                    if (n <= 0) return null; // 데이터 부족 = 손상
                    read += n;
                }
            }

            // 필터 해제 (제자리)
            for (int y = 0; y < height; y++)
            {
                int rowStart = y * (stride + 1);
                int filter = raw[rowStart];
                int cur = rowStart + 1;
                int prev = cur - (stride + 1); // y == 0이면 쓰지 않는다
                switch (filter)
                {
                    case 0: break;
                    case 1: // Sub
                        for (int i = bpp; i < stride; i++)
                            raw[cur + i] = (byte)(raw[cur + i] + raw[cur + i - bpp]);
                        break;
                    case 2: // Up
                        if (y > 0)
                            for (int i = 0; i < stride; i++)
                                raw[cur + i] = (byte)(raw[cur + i] + raw[prev + i]);
                        break;
                    case 3: // Average
                        for (int i = 0; i < stride; i++)
                        {
                            int left = i >= bpp ? raw[cur + i - bpp] : 0;
                            int up = y > 0 ? raw[prev + i] : 0;
                            raw[cur + i] = (byte)(raw[cur + i] + ((left + up) >> 1));
                        }
                        break;
                    case 4: // Paeth
                        for (int i = 0; i < stride; i++)
                        {
                            int a = i >= bpp ? raw[cur + i - bpp] : 0;
                            int b = y > 0 ? raw[prev + i] : 0;
                            int c = (i >= bpp && y > 0) ? raw[prev + i - bpp] : 0;
                            int p = a + b - c;
                            int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                            int pred = (pa <= pb && pa <= pc) ? a : (pb <= pc ? b : c);
                            raw[cur + i] = (byte)(raw[cur + i] + pred);
                        }
                        break;
                    default:
                        return null;
                }
            }

            // RGBA32로 변환하면서 위아래를 뒤집는다(Unity 원시 텍스처는 아래 행부터)
            byte[] rgba = new byte[(long)width * height * 4];
            for (int y = 0; y < height; y++)
            {
                int src = y * (stride + 1) + 1;
                int dst = (height - 1 - y) * width * 4;
                for (int x = 0; x < width; x++, dst += 4)
                {
                    byte r, g, b, a = 255;
                    switch (colorType)
                    {
                        case 6:
                            if (bitDepth == 8) { int o = src + x * 4; r = raw[o]; g = raw[o + 1]; b = raw[o + 2]; a = raw[o + 3]; }
                            else { int o = src + x * 8; r = raw[o]; g = raw[o + 2]; b = raw[o + 4]; a = raw[o + 6]; }
                            break;
                        case 2:
                            if (bitDepth == 8)
                            {
                                int o = src + x * 3; r = raw[o]; g = raw[o + 1]; b = raw[o + 2];
                                if (keyR >= 0 && r == keyR && g == keyG && b == keyB) a = 0;
                            }
                            else
                            {
                                int o = src + x * 6;
                                int r16 = (raw[o] << 8) | raw[o + 1], g16 = (raw[o + 2] << 8) | raw[o + 3], b16 = (raw[o + 4] << 8) | raw[o + 5];
                                r = raw[o]; g = raw[o + 2]; b = raw[o + 4];
                                if (keyR >= 0 && r16 == keyR && g16 == keyG && b16 == keyB) a = 0;
                            }
                            break;
                        case 4:
                            if (bitDepth == 8) { int o = src + x * 2; r = g = b = raw[o]; a = raw[o + 1]; }
                            else { int o = src + x * 4; r = g = b = raw[o]; a = raw[o + 2]; }
                            break;
                        case 0:
                            {
                                int v = Sample(raw, src, x, bitDepth);
                                if (keyR >= 0 && v == keyR) a = 0;
                                r = g = b = ScaleTo8(v, bitDepth);
                            }
                            break;
                        default: // 3 팔레트
                            {
                                int idx = Sample(raw, src, x, bitDepth);
                                if (idx * 3 + 2 >= palette.Length) return null;
                                r = palette[idx * 3]; g = palette[idx * 3 + 1]; b = palette[idx * 3 + 2];
                                if (paletteAlpha != null && idx < paletteAlpha.Length) a = paletteAlpha[idx];
                            }
                            break;
                    }
                    rgba[dst] = r; rgba[dst + 1] = g; rgba[dst + 2] = b; rgba[dst + 3] = a;
                }
            }

            return new DecodedImage { Width = width, Height = height, Rgba = rgba };
        }

        // 한 행 안에서 x번째 샘플(1/2/4/8/16비트) 원래 값
        private static int Sample(byte[] raw, int rowStart, int x, int bitDepth)
        {
            switch (bitDepth)
            {
                case 8: return raw[rowStart + x];
                case 16: return (raw[rowStart + x * 2] << 8) | raw[rowStart + x * 2 + 1];
                default:
                    {
                        int bitPos = x * bitDepth;
                        int bt = raw[rowStart + (bitPos >> 3)];
                        int shift = 8 - bitDepth - (bitPos & 7);
                        return (bt >> shift) & ((1 << bitDepth) - 1);
                    }
            }
        }

        private static byte ScaleTo8(int v, int bitDepth) => bitDepth switch
        {
            1 => (byte)(v * 255),
            2 => (byte)(v * 85),
            4 => (byte)(v * 17),
            16 => (byte)(v >> 8),
            _ => (byte)v
        };
    }
}
