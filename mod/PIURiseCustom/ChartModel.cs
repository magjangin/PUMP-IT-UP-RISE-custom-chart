using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace PIURiseCustom
{
    /// <summary>
    /// 차트 파일(.ucs/.bms/.bme/.bml)을 행 문자열 블록으로 바꾸는 순수 관리 코드 단계 (v0.3.10).
    /// Il2Cpp 객체를 전혀 만들지 않으므로 백그라운드 스레드에서 미리 돌리고 결과를 캐시할 수 있다
    /// (Il2Cpp Step_t 구성은 메인 스레드의 CustomChartParsers.BuildStep이 맡는다).
    /// 로그도 여기서 직접 찍지 않고 Logs에 모아 두었다가 메인 스레드에서 한 번 출력한다(백그라운드 스레드 로깅 회피).
    /// </summary>
    internal sealed class ChartBlock
    {
        public float Bpm = 120f;
        public float DelayMs = 0f;
        public int Beat = 4;
        public int Split = 2;
        public readonly List<string> Rows = new();
    }

    internal sealed class ParsedChart
    {
        public int LaneCount;
        public List<ChartBlock> Blocks;
        public double ParseMs;
        public readonly List<(bool Warn, string Text)> Logs = new();

        public int TotalRows
        {
            get
            {
                int n = 0;
                foreach (var b in Blocks) n += b.Rows.Count;
                return n;
            }
        }

        internal void Msg(string text) => Logs.Add((false, text));
        internal void Warn(string text) => Logs.Add((true, text));
    }

    internal static class ChartModel
    {
        public static ParsedChart Parse(string path, float extraOffsetMs, float minDurationSeconds = 0f)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string ext = Path.GetExtension(path)?.ToLowerInvariant();
            ParsedChart chart;
            if (ext == ".ucs")
                chart = ParseUcs(path, extraOffsetMs, minDurationSeconds);
            else if (ext == ".bms" || ext == ".bme" || ext == ".bml")
                chart = ParseBms(path, extraOffsetMs, minDurationSeconds);
            else
                throw new NotSupportedException($"지원하지 않는 차트 확장자: {ext}");
            chart.ParseMs = sw.Elapsed.TotalMilliseconds;
            return chart;
        }

        private static void PadBlocksToMinDuration(ParsedChart chart, float minDurationSeconds)
        {
            var blocks = chart.Blocks;
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

            string emptyRow = new string('.', chart.LaneCount);
            for (int i = 0; i < extraRows; i++)
                last.Rows.Add(emptyRow);

            chart.Msg($"[ChartPad] 채보 끝({totalMs / 1000.0:0.1}s) → BGM 막바지({minDurationSeconds:0.1}s)까지 빈 행 {extraRows}줄 패딩");
        }

        /// <summary>
        /// 차트 파일이 없는 뼈대 테스트 폴더용 빈 차트 (노트 0개, BGM/BGA 테스트용).
        /// </summary>
        public static ParsedChart CreateEmpty(int laneCount = 5, float bpm = 120f, float durationSeconds = 300f)
        {
            const int split = 2;
            float clampedBpm = Math.Clamp(bpm, 60f, 300f);
            int totalRows = Math.Max(16, (int)Math.Ceiling((durationSeconds / 60f) * clampedBpm * split));
            string emptyRow = new string('.', laneCount);

            var blk = new ChartBlock
            {
                Bpm = clampedBpm,
                DelayMs = 0f,
                Beat = 4,
                Split = split
            };
            for (int i = 0; i < totalRows; i++)
                blk.Rows.Add(emptyRow);

            return new ParsedChart { LaneCount = laneCount, Blocks = new List<ChartBlock> { blk } };
        }

        /// <summary>
        /// 표준 펌프 잇 업 UCS 파서 (단일/멀티블록 변속 지원).
        /// </summary>
        public static ParsedChart ParseUcs(string path, float extraOffsetMs, float minDurationSeconds = 0f)
        {
            string[] lines = Util.ReadAllLinesSmart(path);
            int laneCount = 5;
            var blocks = new List<ChartBlock>();
            ChartBlock cur = null;
            bool readingHeaders = true;

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0)
                    continue;

                if (line[0] == ':')
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
                        cur = new ChartBlock();
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
                        cur = new ChartBlock();
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
            var chart = new ParsedChart { LaneCount = laneCount, Blocks = blocks };
            PadBlocksToMinDuration(chart, minDurationSeconds);
            return chart;
        }

        // BMS 노트 하나. beat는 (마디 시작 박 + 마디 안 위치 × 마디 길이)로, 정렬·분할 선택·행 계산에 같은 값을 재사용한다.
        // Order는 파일 내 출현 순서로, 같은 박에 여러 노트가 올 때 안정 정렬(Stable Sort)을 보장한다(v0.3.16).
        private struct BmsNote
        {
            public int Measure;
            public int Lane;
            public double Frac;
            public string Id;
            public double Beat;
            public int Order;
        }

        /// <summary>
        /// BMS(.bms/.bme/.bml)를 펌프 잇 업 5키(Single) 행 블록으로 변환한다.
        /// 틱(박) 기반 시간 계산(time = beat * 60000 / bpm, 1마디 = 4박 기준) 사용.
        /// </summary>
        public static ParsedChart ParseBms(string path, float extraOffsetMs, float minDurationSeconds = 0f)
        {
            // UTF-8 → CP949(EUC-KR) → Shift-JIS 자동 판별 (데이터 라인은 ASCII라 인코딩과 무관, 헤더 문자열만 영향)
            string[] lines = Util.ReadAllLinesSmart(path);
            string fileName = Path.GetFileName(path);

            double baseBpm = 120.0;
            var extBpm = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var measureLen = new Dictionary<int, double>();
            var holdStartWavIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var holdEndWavIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // 롱노트는 키음 이름(#WAVxx 홀드 시작/끝) 방식만 쓴다. 표준 BMS 롱노트(LNTYPE 1의 5x 채널, #LNOBJ)는
            // 사용자가 쓰지 않아 지원하지 않는다(v0.3.5에 들어갔다가 v0.3.7에서 제거). 5x 채널은 무시되고 #LNOBJ 끝 표식은 단타로 읽힌다.
            var rawNotes = new List<BmsNote>();
            var bpmChanges = new List<(int measure, double frac, double bpm)>();
            int maxMeasure = 0;
            int wavKeyLen = 2; // 2자리(#WAV01) 기본, 3자리(#WAV001) 감지 시 자동 전환
            int extBpmKeyLen = 2; // #BPM01 / #BPM001 (채널 08 토큰 길이). #WAV 자릿수와 따로 본다
            // 데이터 라인은 헤더를 다 읽은 뒤 처리한다(v0.3.13). 예전엔 한 번에 읽어서, 헤더가 데이터 뒤에 오거나 섞인 파일에서
            // #WAV/#BPM 자릿수(토큰 길이)·확장 BPM 값·홀드 키음 이름을 아직 모르는 채로 앞쪽 데이터를 잘랐다.
            // 헤더가 앞에 모인 보통 파일은 처리 순서가 같아 결과도 같다.
            var dataLines = new List<string>();

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] != '#')
                    continue;
                // 탭으로 구분한 헤더(#BPM\t140, #WAV01\t홀드 시작.wav)도 공백과 똑같이 읽는다
                if (line.IndexOf('\t') >= 0)
                    line = line.Replace('\t', ' ');

                // 데이터 라인(#MMMCC:DATA)이 대부분이므로 먼저 판별한다. 헤더(#BPM/#WAV …)는 7번째 글자가 ':'일 수 없다
                // (#BPMxx / #WAVxx 키는 6~7번째 자리에 공백이 오고, #BPM 140은 5번째가 공백).
                if (line.Length > 6 && line[6] == ':' && line.IndexOf(':') == 6)
                {
                    dataLines.Add(line);
                    continue;
                }

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
                        if (key.Length == 3) extBpmKeyLen = 3;
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
            }

            foreach (string dataLine in dataLines)
                ParseBmsDataLine(dataLine, ref maxMeasure, measureLen, extBpm, wavKeyLen, extBpmKeyLen, bpmChanges, rawNotes);

            if (rawNotes.Count == 0)
            {
                var empty = CreateEmpty(5, (float)baseBpm, 300f);
                empty.Msg($"[BMS Parser] '{fileName}' -> 비어있는 BMS (BPM={baseBpm}), 빈 Step_t 생성");
                return empty;
            }

            const int laneCount = 5;

            // 마디별 누적 박(beat, 1마디 = 4.0 * ratio 박) 계산
            double[] measureStartBeat = new double[maxMeasure + 2];
            for (int m = 0; m <= maxMeasure; m++)
            {
                double len = measureLen.TryGetValue(m, out double r) ? r : 1.0;
                measureStartBeat[m + 1] = measureStartBeat[m] + 4.0 * len;
            }

            // 노트마다 박 위치를 한 번만 계산해 둔다(예전엔 정렬 비교·분할 선택·행 배치에서 매번 다시 계산했다)
            for (int i = 0; i < rawNotes.Count; i++)
            {
                var n = rawNotes[i];
                n.Beat = measureStartBeat[n.Measure] + n.Frac * (measureStartBeat[n.Measure + 1] - measureStartBeat[n.Measure]);
                rawNotes[i] = n;
            }

            // 1박당 분할 수(split): 노트·변속 위치를 정확히 표현하는 가장 작은 값을 고른다.
            // 예전엔 16 고정이라 (1) 셋잇단(12·24분 등)이 반올림으로 어긋나고 (2) 8비트 위주 차트도 행이 2배로 늘어
            // 행×레인마다 Il2Cpp Note 객체를 만드는 BuildStep이 불필요하게 무거웠다.
            var chart = new ParsedChart { LaneCount = laneCount };
            int split = ChooseBeatSplit(chart, rawNotes, bpmChanges, measureStartBeat);
            double totalBeats = measureStartBeat[maxMeasure + 1];
            int totalRows = (int)Math.Ceiling(totalBeats * split) + split * 4;

            // 그리드: 행마다 레인 수만큼의 char 배열 (마지막에 그대로 행 문자열로 만든다)
            char[] grid = new char[totalRows * laneCount];
            Array.Fill(grid, '.');

            // 롱노트 페어링용: 키음 이름(홀드 시작/끝)
            var lnOpenRow = new int[laneCount];
            Array.Fill(lnOpenRow, -1);
            int orphanHolds = 0;

            void PlaceHold(int ln, int s, int e)
            {
                if (e <= s)
                {
                    grid[s * laneCount + ln] = 'X';
                    return;
                }
                grid[s * laneCount + ln] = 'M';
                for (int r = s + 1; r < e; r++)
                    grid[r * laneCount + ln] = 'H';
                grid[e * laneCount + ln] = 'W';
            }

            // 짝이 없는 홀드 시작은 예전엔 노트가 통째로 사라졌다 → 단타로 남긴다
            void CloseOrphan(int[] open, int ln)
            {
                if (open[ln] < 0) return;
                if (grid[open[ln] * laneCount + ln] == '.') grid[open[ln] * laneCount + ln] = 'X';
                open[ln] = -1;
                orphanHolds++;
            }

            // 박 순서로 처리 (같은 박의 노트 순서는 파일 출현 순서 Order를 보존해 안정 정렬 보장, v0.3.16)
            rawNotes.Sort((a, b) =>
            {
                int cmp = a.Beat.CompareTo(b.Beat);
                return cmp != 0 ? cmp : a.Order.CompareTo(b.Order);
            });

            foreach (var n in rawNotes)
            {
                int row = Math.Clamp((int)Math.Round(n.Beat * split), 0, totalRows - 1);
                int cell = row * laneCount + n.Lane;

                if (holdStartWavIds.Contains(n.Id))
                {
                    CloseOrphan(lnOpenRow, n.Lane); // 끝 없이 다시 시작하면 앞의 시작은 단타
                    lnOpenRow[n.Lane] = row;
                }
                else if (holdEndWavIds.Contains(n.Id))
                {
                    if (lnOpenRow[n.Lane] >= 0)
                    {
                        PlaceHold(n.Lane, lnOpenRow[n.Lane], row);
                        lnOpenRow[n.Lane] = -1;
                    }
                    else if (grid[cell] == '.')
                    {
                        grid[cell] = 'X';
                    }
                }
                else
                {
                    if (grid[cell] == '.')
                        grid[cell] = 'X';
                }
            }

            for (int c = 0; c < laneCount; c++)
                CloseOrphan(lnOpenRow, c);
            if (orphanHolds > 0)
                chart.Warn($"[BMS Parser] '{fileName}' 홀드 끝 키음이 없는 롱노트 시작 {orphanHolds}개 → 단타로 배치");

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
            var blocks = new List<ChartBlock>();
            // 빈 행은 문자열 하나를 같이 쓴다(차트 대부분이 빈 행이라 문자열 할당이 크게 준다)
            string emptyRow = new string('.', laneCount);

            for (int bi = 0; bi < blockStarts.Count; bi++)
            {
                int startRow = blockStarts[bi];
                int endRow = (bi + 1 < blockStarts.Count) ? blockStarts[bi + 1] : totalRows;
                if (endRow <= startRow) continue;

                var blk = new ChartBlock
                {
                    Bpm = (float)Math.Clamp(bpmEvents[startRow], 0.1, 999.0),
                    DelayMs = bi == 0 ? extraOffsetMs : 0f,
                    Beat = 4,
                    Split = split
                };
                blk.Rows.Capacity = endRow - startRow;

                for (int r = startRow; r < endRow; r++)
                {
                    int off = r * laneCount;
                    bool empty = true;
                    for (int c = 0; c < laneCount; c++)
                    {
                        if (grid[off + c] != '.') { empty = false; break; }
                    }
                    blk.Rows.Add(empty ? emptyRow : new string(grid, off, laneCount));
                }

                blocks.Add(blk);
            }

            chart.Blocks = blocks;
            PadBlocksToMinDuration(chart, minDurationSeconds);
            chart.Msg($"[BMS Parser] '{fileName}' -> {laneCount}키, BPM={baseBpm}, Split={split}, 블록 {blocks.Count}개, 총 {totalRows}행 변환 완료");
            return chart;
        }

        private static void ParseBmsDataLine(
            string line, ref int maxMeasure, Dictionary<int, double> measureLen, Dictionary<string, double> extBpm,
            int wavKeyLen, int extBpmKeyLen, List<(int measure, double frac, double bpm)> bpmChanges, List<BmsNote> rawNotes)
        {
            if (!int.TryParse(line.AsSpan(1, 3), NumberStyles.Integer, CultureInfo.InvariantCulture, out int measure))
                return;

            char c0 = char.ToUpperInvariant(line[4]);
            char c1 = char.ToUpperInvariant(line[5]);
            string data = line.Substring(7).Trim();
            if (data.Length == 0)
                return;

            if (measure > maxMeasure) maxMeasure = measure;

            // 마디 길이 배율 (채널 02)
            if (c0 == '0' && c1 == '2')
            {
                if (double.TryParse(data, NumberStyles.Float, CultureInfo.InvariantCulture, out double ratio) && ratio > 0)
                    measureLen[measure] = ratio;
                return;
            }

            bool isHexBpm = c0 == '0' && c1 == '3';
            bool isExtBpm = c0 == '0' && c1 == '8';
            int lane = MapBmsChannelToPiuLane(c0, c1);
            if (!isHexBpm && !isExtBpm && lane < 0)
                return; // 쓰지 않는 채널은 토큰을 자르지도 않는다

            // 토큰 길이: 03(16진 BPM)은 항상 2자리, 08은 #BPMxx 자릿수, 나머지는 #WAVxx 자릿수.
            // 예전엔 3자리 #WAV 차트에서 03 채널 "780000"을 "780"/"000"으로 잘라 BPM 1920으로 읽었다.
            int tokLen;
            if (isHexBpm) tokLen = 2;
            else if (isExtBpm) tokLen = (extBpmKeyLen == 3 && data.Length % 3 == 0) ? 3 : 2;
            else tokLen = (wavKeyLen == 3 && data.Length % 3 == 0) ? 3 : 2;
            int count = data.Length / tokLen;
            if (count <= 0) return;

            for (int i = 0; i < count; i++)
            {
                int at = i * tokLen;
                if (IsZeroToken(data, at, tokLen))
                    continue; // 빈 칸("00")은 문자열을 만들지 않고 건너뛴다

                double frac = (double)i / count;
                if (isHexBpm)
                {
                    if (int.TryParse(data.AsSpan(at, tokLen), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hexBpm) && hexBpm > 0)
                        bpmChanges.Add((measure, frac, hexBpm));
                }
                else if (isExtBpm)
                {
                    if (extBpm.TryGetValue(data.Substring(at, tokLen), out double eb))
                        bpmChanges.Add((measure, frac, eb));
                }
                else
                {
                    rawNotes.Add(new BmsNote { Measure = measure, Lane = lane, Frac = frac, Id = data.Substring(at, tokLen), Order = rawNotes.Count });
                }
            }
        }

        private static bool IsZeroToken(string s, int at, int len)
        {
            for (int k = 0; k < len; k++)
                if (s[at + k] != '0') return false;
            return true;
        }

        // 1박당 분할 후보 (4분 기준 4=16분, 12=셋잇단 16분, 48=192분 해상도 …). nBeatSplit은 byte, UCS Split 상한 128.
        private static readonly int[] SplitCandidates = { 4, 8, 12, 16, 24, 32, 48, 64, 96 };
        private const int FallbackSplit = 48;

        private static int ChooseBeatSplit(
            ParsedChart chart,
            List<BmsNote> notes,
            List<(int measure, double frac, double bpm)> bpmChanges,
            double[] measureStartBeat)
        {
            var beats = new double[notes.Count + bpmChanges.Count];
            int k = 0;
            foreach (var n in notes) beats[k++] = n.Beat;
            foreach (var b in bpmChanges)
                beats[k++] = measureStartBeat[b.measure] + b.frac * (measureStartBeat[b.measure + 1] - measureStartBeat[b.measure]);

            foreach (int s in SplitCandidates)
            {
                bool exact = true;
                foreach (double beat in beats)
                {
                    double v = beat * s;
                    if (Math.Abs(v - Math.Round(v)) > 1e-4)
                    {
                        exact = false;
                        break;
                    }
                }
                if (exact)
                    return s;
            }

            chart.Msg($"[BMS Parser] 정확히 맞는 분할이 없어 Split={FallbackSplit}로 반올림 배치 (5·7연음 등)");
            return FallbackSplit;
        }

        private static int MapBmsChannelToPiuLane(char c0, char c1)
        {
            // 5키 전용 (에디터 좌→우 5레인: 16, 11, 12, 13, 14 -> 펌프 0~4: ↙ ↖ ◎ ↗ ↘)
            if (c0 != '1') return -1;
            switch (c1)
            {
                case '6': return 0;
                case '1': return 1;
                case '2': return 2;
                case '3': return 3;
                case '4': return 4;
                default: return -1;
            }
        }
    }
}
