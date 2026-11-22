using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPIUMobileStepDLL;

namespace PIURiseCustom
{
    /// <summary>
    /// UCS(.ucs) 및 BMS(.bms/.bme/.bml) 차트를 PUMP IT UP RISE의 Il2Cpp Step_t 구조로 직접 파싱한다.
    /// 게임 내장 StepLoader.LoadUserStep의 멀티블록(변속) 버그를 회피하고 모든 변속/롱노트를 지원한다.
    /// </summary>
    internal static class CustomChartParsers
    {
        // StepLoader.LoadUserStep (RVA 0x7A4FF0) 의사코드에서 확인된 mAttr 바이트 값
        private const byte ATTR_EMPTY = 0x00; // '.'
        private const byte ATTR_TAP = 0x43;   // 'X'
        private const byte ATTR_HOLD_HEAD = 0x57; // 'M'
        private const byte ATTR_HOLD_BODY = 0x5B; // 'H'
        private const byte ATTR_HOLD_TAIL = 0x5F; // 'W'
        private const byte DEFAULT_VISUAL_EFFECT = 0x03;

        public static Step_t LoadFromFile(string path, float extraOffsetMs, float minDurationSeconds = 0f)
        {
            string ext = Path.GetExtension(path)?.ToLowerInvariant();
            if (ext == ".ucs")
                return ParseUcs(path, extraOffsetMs, minDurationSeconds);
            if (ext == ".bms" || ext == ".bme" || ext == ".bml")
                return ParseBms(path, extraOffsetMs, minDurationSeconds);

            throw new NotSupportedException($"지원하지 않는 차트 확장자: {ext}");
        }

        /// <summary>
        /// OGG Vorbis / WAV 파일의 실제 재생 길이(초)를 빠르게 읽는다 (PERFECT GAME 조기 표시 방지용).
        /// </summary>
        public static float TryGetAudioDurationSeconds(string audioPath)
        {
            if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
                return 0f;

            try
            {
                string ext = Path.GetExtension(audioPath)?.ToLowerInvariant();
                using var fs = File.OpenRead(audioPath);

                if (ext == ".ogg")
                {
                    // 앞부분에서 \x01vorbis 헤더의 샘플레이트 읽기
                    int headLen = (int)Math.Min(fs.Length, 4096);
                    byte[] head = new byte[headLen];
                    fs.Read(head, 0, headLen);
                    int sampleRate = 0;
                    for (int i = 0; i <= headLen - 16; i++)
                    {
                        if (head[i] == 0x01 && head[i + 1] == 'v' && head[i + 2] == 'o' &&
                            head[i + 3] == 'r' && head[i + 4] == 'b' && head[i + 5] == 'i' && head[i + 6] == 's')
                        {
                            sampleRate = BitConverter.ToInt32(head, i + 12);
                            break;
                        }
                    }
                    if (sampleRate <= 0) return 0f;

                    // 끝부분 64KB에서 마지막 OggS 페이지의 granule position 읽기
                    int tailLen = (int)Math.Min(fs.Length, 65536);
                    fs.Position = fs.Length - tailLen;
                    byte[] tail = new byte[tailLen];
                    fs.Read(tail, 0, tailLen);
                    long lastGranule = -1;
                    for (int i = tailLen - 14; i >= 0; i--)
                    {
                        if (tail[i] == 'O' && tail[i + 1] == 'g' && tail[i + 2] == 'g' && tail[i + 3] == 'S')
                        {
                            long g = BitConverter.ToInt64(tail, i + 6);
                            if (g > 0)
                            {
                                lastGranule = g;
                                break;
                            }
                        }
                    }
                    if (lastGranule > 0)
                        return (float)((double)lastGranule / sampleRate);
                }
            }
            catch { }
            return 0f;
        }

        private static void PadBlocksToMinDuration(int laneCount, List<UcsBlockData> blocks, float minDurationSeconds)
        {
            if (minDurationSeconds <= 1f || blocks == null || blocks.Count == 0)
                return;

            double totalMs = 0.0;
            foreach (var b in blocks)
            {
                double msPerLine = 60000.0 / (b.Split * b.Bpm);
                totalMs += b.DelayMs + b.Rows.Count * msPerLine;
            }

            double targetMs = minDurationSeconds * 1000.0;
            if (totalMs >= targetMs)
                return;

            var last = blocks[blocks.Count - 1];
            double lastMsPerLine = 60000.0 / (last.Split * last.Bpm);
            int extraRows = (int)Math.Ceiling((targetMs - totalMs) / lastMsPerLine);
            if (extraRows <= 0)
                return;

            string emptyRow = new string('.', laneCount);
            for (int i = 0; i < extraRows; i++)
                last.Rows.Add(emptyRow);

            Mod.Log?.Msg($"[ChartPad] 채보 끝({totalMs / 1000.0:0.1}s) → BGM 막바지({minDurationSeconds:0.1}s)까지 빈 행 {extraRows}줄 패딩");
        }

        /// <summary>
        /// 차트 파일이 없는 뼈대 테스트 폴더용 빈 Step_t 생성 (노트 0개, BGM/BGA 테스트용).
        /// </summary>
        public static Step_t CreateEmptyStep(int laneCount = 5, float bpm = 120f, float durationSeconds = 300f)
        {
            const int split = 2;
            float clampedBpm = Math.Clamp(bpm, 60f, 300f);
            int totalRows = Math.Max(16, (int)Math.Ceiling((durationSeconds / 60f) * clampedBpm * split));
            string emptyRow = new string('.', laneCount);

            var blk = new UcsBlockData
            {
                Bpm = clampedBpm,
                DelayMs = 0f,
                Beat = 4,
                Split = split
            };
            for (int i = 0; i < totalRows; i++)
                blk.Rows.Add(emptyRow);

            return BuildStepFromBlocks(laneCount, new List<UcsBlockData> { blk });
        }

        private sealed class UcsBlockData
        {
            public float Bpm = 120f;
            public float DelayMs = 0f;
            public int Beat = 4;
            public int Split = 2;
            public readonly List<string> Rows = new();
        }

        /// <summary>
        /// 표준 펌프 잇 업 UCS 파서 (단일/멀티블록 변속 완벽 지원).
        /// </summary>
        public static Step_t ParseUcs(string path, float extraOffsetMs, float minDurationSeconds = 0f)
        {
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            int laneCount = 5;
            var blocks = new List<UcsBlockData>();
            UcsBlockData cur = null;
            bool readingHeaders = true;

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                if (line.StartsWith(":", StringComparison.Ordinal))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(1, eq - 1).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    if (key.Equals("Format", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (key.Equals("Mode", StringComparison.OrdinalIgnoreCase))
                    {
                        // Double / D-Performance 등은 10레인, Single / S-Performance는 5레인
                        laneCount = val.IndexOf('D', StringComparison.OrdinalIgnoreCase) >= 0 ? 10 : 5;
                        continue;
                    }

                    if (!readingHeaders || cur == null)
                    {
                        cur = new UcsBlockData();
                        if (blocks.Count > 0)
                        {
                            var prev = blocks[blocks.Count - 1];
                            cur.Bpm = prev.Bpm;
                            cur.Beat = prev.Beat;
                            cur.Split = prev.Split;
                        }
                        blocks.Add(cur);
                        readingHeaders = true;
                    }

                    if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float fval))
                    {
                        if (key.Equals("BPM", StringComparison.OrdinalIgnoreCase))
                            cur.Bpm = Math.Clamp(fval, 0.1f, 999f);
                        else if (key.Equals("Delay", StringComparison.OrdinalIgnoreCase))
                            cur.DelayMs = Math.Clamp(fval, -999999f, 999999f);
                        else if (key.Equals("Beat", StringComparison.OrdinalIgnoreCase))
                            cur.Beat = Math.Clamp((int)fval, 1, 64);
                        else if (key.Equals("Split", StringComparison.OrdinalIgnoreCase))
                            cur.Split = Math.Clamp((int)fval, 1, 128);
                    }
                }
                else
                {
                    if (cur == null)
                    {
                        cur = new UcsBlockData();
                        blocks.Add(cur);
                    }
                    readingHeaders = false;
                    cur.Rows.Add(line);
                }
            }

            if (blocks.Count == 0)
                throw new InvalidDataException("UCS 파일에 블록 데이터가 없습니다.");

            // 첫 블록에만 사용자 공통 오프셋(extraOffsetMs) 적용
            blocks[0].DelayMs += extraOffsetMs;
            PadBlocksToMinDuration(laneCount, blocks, minDurationSeconds);

            return BuildStepFromBlocks(laneCount, blocks);
        }

        /// <summary>
        /// BMS(.bms/.bme/.bml)를 펌프 잇 업 5키(Single) Step_t로 변환한다.
        /// 틱(박) 기반 시간 계산(time = beat * 60000 / bpm, 1마디 = 4박 기준) 사용.
        /// </summary>
        public static Step_t ParseBms(string path, float extraOffsetMs, float minDurationSeconds = 0f)
        {
            // Shift-JIS / UTF-8 / EUC-KR 호환을 위해 기본 UTF8로 읽고 실패 시 Default
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);

            double baseBpm = 120.0;
            var extBpm = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var measureLen = new Dictionary<int, double>();
            var holdStartWavIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var holdEndWavIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // (measure, lane 0..9, posInMeasure 0..1, objId)
            var rawNotes = new List<(int measure, int lane, double frac, string id)>();
            var bpmChanges = new List<(int measure, double frac, double bpm)>();
            int maxMeasure = 0;
            int wavKeyLen = 2; // 2자리(#WAV01) 기본, 3자리(#WAV001) 감지 시 자동 전환

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (!line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                // 헤더: #BPM 140
                if (line.StartsWith("#BPM ", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(line.Substring(5).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double b) && b > 0)
                        baseBpm = b;
                    continue;
                }

                // 확장 BPM: #BPM01 180.5 또는 #BPM001 180.5
                if (line.StartsWith("#BPM", StringComparison.OrdinalIgnoreCase) && line.Length > 6)
                {
                    int sp = line.IndexOf(' ');
                    if (sp == 6 || sp == 7)
                    {
                        string key = line.Substring(4, sp - 4).Trim();
                        if (double.TryParse(line.Substring(sp + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double b) && b > 0)
                            extBpm[key] = b;
                        continue;
                    }
                }

                // 키음 정의: #WAV06 / #WAV006 홀드 시작.wav, #WAV07 / #WAV007 홀드 끝.wav (2자리/3자리 모두 지원)
                if (line.StartsWith("#WAV", StringComparison.OrdinalIgnoreCase) && line.Length > 6)
                {
                    int sp = line.IndexOf(' ');
                    if (sp > 4)
                    {
                        string key = line.Substring(4, sp - 4).Trim();
                        if (key.Length == 3) wavKeyLen = 3;
                        string wavName = line.Substring(sp + 1).Trim();
                        string norm = wavName.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", "");
                        if (norm.Contains("홀드시작") || norm.Contains("holdstart") || norm.Contains("longstart") || norm.Contains("lnstart"))
                            holdStartWavIds.Add(key);
                        else if (norm.Contains("홀드끝") || norm.Contains("holdend") || norm.Contains("longend") || norm.Contains("lnend"))
                            holdEndWavIds.Add(key);
                        continue;
                    }
                }

                // 데이터 라인: #MMMCC:DATA
                int colon = line.IndexOf(':');
                if (colon != 6)
                    continue;

                if (!int.TryParse(line.Substring(1, 3), out int measure))
                    continue;

                string channel = line.Substring(4, 2).ToUpperInvariant();
                string data = line.Substring(7).Trim();
                if (string.IsNullOrEmpty(data))
                    continue;

                if (measure > maxMeasure) maxMeasure = measure;

                // 마디 길이 배율 (채널 02)
                if (channel == "02")
                {
                    if (double.TryParse(data, NumberStyles.Float, CultureInfo.InvariantCulture, out double ratio) && ratio > 0)
                        measureLen[measure] = ratio;
                    continue;
                }

                int tokLen = (wavKeyLen == 3 && data.Length % 3 == 0) ? 3 : 2;
                string zeroTok = tokLen == 3 ? "000" : "00";
                int count = data.Length / tokLen;
                if (count <= 0) continue;

                // BPM 변경 (03: 16진수, 08: 확장 BPM)
                if (channel == "03" || channel == "08")
                {
                    for (int i = 0; i < count; i++)
                    {
                        string obj = data.Substring(i * tokLen, tokLen);
                        if (obj == zeroTok) continue;
                        double frac = (double)i / count;
                        if (channel == "03")
                        {
                            if (int.TryParse(obj, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hexBpm) && hexBpm > 0)
                                bpmChanges.Add((measure, frac, hexBpm));
                        }
                        else if (extBpm.TryGetValue(obj, out double eb))
                        {
                            bpmChanges.Add((measure, frac, eb));
                        }
                    }
                    continue;
                }

                // 5키 일반 노트 채널 매핑 (16, 11, 12, 13, 14 / 홀드 키음 매칭으로 처리)
                int lane = MapBmsChannelToPiuLane(channel);
                if (lane < 0) continue;

                for (int i = 0; i < count; i++)
                {
                    string obj = data.Substring(i * tokLen, tokLen);
                    if (obj == zeroTok) continue;
                    rawNotes.Add((measure, lane, (double)i / count, obj));
                }
            }

            if (rawNotes.Count == 0)
            {
                Mod.Log?.Msg($"[BMS Parser] '{Path.GetFileName(path)}' -> 비어있는 BMS (BPM={baseBpm}), 빈 Step_t 생성");
                return CreateEmptyStep(5, (float)baseBpm, 300f);
            }

            const int laneCount = 5;

            // 마디별 누적 박(beat, 1마디 = 4.0 * ratio 박) 계산
            double[] measureStartBeat = new double[maxMeasure + 2];
            for (int m = 0; m <= maxMeasure; m++)
            {
                double len = measureLen.TryGetValue(m, out double r) ? r : 1.0;
                measureStartBeat[m + 1] = measureStartBeat[m] + 4.0 * len;
            }

            // 1박당 분할 수(split) = 16 (64분음표 해상도)
            const int split = 16;
            double totalBeats = measureStartBeat[maxMeasure + 1];
            int totalRows = (int)Math.Ceiling(totalBeats * split) + split * 4;

            // 그리드 초기화
            char[,] grid = new char[totalRows, laneCount];
            for (int r = 0; r < totalRows; r++)
                for (int c = 0; c < laneCount; c++)
                    grid[r, c] = '.';

            // 롱노트 페어링용
            var lnOpenRow = new int[laneCount];
            Array.Fill(lnOpenRow, -1);

            rawNotes.Sort((a, b) =>
            {
                double ba = measureStartBeat[a.measure] + a.frac * (measureStartBeat[a.measure + 1] - measureStartBeat[a.measure]);
                double bb = measureStartBeat[b.measure] + b.frac * (measureStartBeat[b.measure + 1] - measureStartBeat[b.measure]);
                return ba.CompareTo(bb);
            });

            foreach (var n in rawNotes)
            {
                double mBeats = measureStartBeat[n.measure + 1] - measureStartBeat[n.measure];
                double beatPos = measureStartBeat[n.measure] + n.frac * mBeats;
                int row = Math.Clamp((int)Math.Round(beatPos * split), 0, totalRows - 1);

                if (holdStartWavIds.Contains(n.id))
                {
                    lnOpenRow[n.lane] = row;
                }
                else if (holdEndWavIds.Contains(n.id))
                {
                    if (lnOpenRow[n.lane] >= 0)
                    {
                        int startRow = lnOpenRow[n.lane];
                        lnOpenRow[n.lane] = -1;
                        if (row <= startRow)
                        {
                            grid[startRow, n.lane] = 'X';
                        }
                        else
                        {
                            grid[startRow, n.lane] = 'M';
                            for (int r = startRow + 1; r < row; r++)
                                grid[r, n.lane] = 'H';
                            grid[row, n.lane] = 'W';
                        }
                    }
                    else if (grid[row, n.lane] == '.')
                    {
                        grid[row, n.lane] = 'X';
                    }
                }
                else
                {
                    if (grid[row, n.lane] == '.')
                        grid[row, n.lane] = 'X';
                }
            }

            // BPM 변경 지점을 행(row) 기준으로 정렬하여 블록 분할
            var bpmEvents = new SortedDictionary<int, double>();
            bpmEvents[0] = baseBpm;
            foreach (var bc in bpmChanges)
            {
                double mBeats = measureStartBeat[bc.measure + 1] - measureStartBeat[bc.measure];
                double beatPos = measureStartBeat[bc.measure] + bc.frac * mBeats;
                int row = Math.Clamp((int)Math.Round(beatPos * split), 0, totalRows - 1);
                bpmEvents[row] = bc.bpm;
            }

            var blockStarts = new List<int>(bpmEvents.Keys);
            var blocks = new List<UcsBlockData>();

            for (int bi = 0; bi < blockStarts.Count; bi++)
            {
                int startRow = blockStarts[bi];
                int endRow = (bi + 1 < blockStarts.Count) ? blockStarts[bi + 1] : totalRows;
                if (endRow <= startRow) continue;

                var blk = new UcsBlockData
                {
                    Bpm = (float)Math.Clamp(bpmEvents[startRow], 0.1, 999.0),
                    DelayMs = bi == 0 ? extraOffsetMs : 0f,
                    Beat = 4,
                    Split = split
                };

                var sb = new StringBuilder(laneCount);
                for (int r = startRow; r < endRow; r++)
                {
                    sb.Clear();
                    for (int c = 0; c < laneCount; c++)
                        sb.Append(grid[r, c]);
                    blk.Rows.Add(sb.ToString());
                }

                blocks.Add(blk);
            }

            PadBlocksToMinDuration(laneCount, blocks, minDurationSeconds);
            Mod.Log?.Msg($"[BMS Parser] '{Path.GetFileName(path)}' -> {laneCount}키, BPM={baseBpm}, 블록 {blocks.Count}개, 총 {totalRows}행 변환 완료");
            return BuildStepFromBlocks(laneCount, blocks);
        }

        private static int MapBmsChannelToPiuLane(string ch)
        {
            switch (ch)
            {
                // 5키 전용 (에디터 좌→우 5레인: 16, 11, 12, 13, 14 -> 펌프 0~4: ↙ ↖ ◎ ↗ ↘)
                case "16": return 0;
                case "11": return 1;
                case "12": return 2;
                case "13": return 3;
                case "14": return 4;
                default: return -1;
            }
        }

        private static Step_t BuildStepFromBlocks(int laneCount, List<UcsBlockData> blocks)
        {
            var step = new Step_t
            {
                nNoteOffset = 0,
                nNote = laneCount,
                bDirect = 0,
                nBlock = blocks.Count
            };

            var ilBlocks = new Il2CppReferenceArray<Block>(blocks.Count);
            float curTimeMs = 0f;

            for (int bi = 0; bi < blocks.Count; bi++)
            {
                var b = blocks[bi];
                float msPerLine = 60000f / (b.Split * b.Bpm);
                float startMs = curTimeMs + b.DelayMs;

                var div = new Div
                {
                    msStart = startMs,
                    _BPM = msPerLine, // StepLoader.LoadUserStep 규약: +0x14 필드에 msPerLine 저장
                    BeatPerLine = 1f / b.Split,
                    _msGap = b.DelayMs / (b.Split * msPerLine),
                    Speed = 1f,
                    nBeatSplit = (byte)b.Split,
                    nBeatPerMeasure = (byte)b.Beat,
                    bmFlags = 0,
                    nLine = b.Rows.Count
                };

                var lineList = new Il2CppSystem.Collections.Generic.List<Il2CppReferenceArray<Note>>(b.Rows.Count);

                for (int ri = 0; ri < b.Rows.Count; ri++)
                {
                    string rowStr = b.Rows[ri];
                    var noteArr = new Il2CppReferenceArray<Note>(laneCount);
                    for (int c = 0; c < laneCount; c++)
                    {
                        char ch = c < rowStr.Length ? rowStr[c] : '.';
                        byte attr = ch switch
                        {
                            'X' or 'x' => ATTR_TAP,
                            'M' or 'm' => ATTR_HOLD_HEAD,
                            'H' or 'h' => ATTR_HOLD_BODY,
                            'W' or 'w' => ATTR_HOLD_TAIL,
                            _ => ATTR_EMPTY
                        };

                        var note = new Note
                        {
                            mAttr = attr,
                            mVisualEffect = DEFAULT_VISUAL_EFFECT,
                            mBank_Param = 0
                        };
                        noteArr[c] = note;
                    }
                    lineList.Add(noteArr);
                }

                div.pLine = lineList;

                var block = new Block
                {
                    DivSel = 0,
                    nDiv = 1
                };
                var divArr = new Il2CppReferenceArray<Div>(1);
                divArr[0] = div;
                block.pDiv = divArr;

                ilBlocks[bi] = block;
                curTimeMs = startMs + (b.Rows.Count * msPerLine);
            }

            step.pBlock = ilBlocks;
            return step;
        }
    }
}
