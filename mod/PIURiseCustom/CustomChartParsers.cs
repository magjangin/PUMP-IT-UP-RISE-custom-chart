using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppPIUMobileStepDLL;

namespace PIURiseCustom
{
    /// <summary>
    /// UCS(.ucs) 및 BMS(.bms/.bme/.bml) 차트를 PUMP IT UP RISE의 Il2Cpp Step_t 구조로 만든다.
    /// 게임 내장 StepLoader.LoadUserStep의 멀티블록(변속) 버그를 회피하고 모든 변속/롱노트를 지원한다.
    ///
    /// v0.3.10: 두 단계로 나눴다.
    ///  1) 파일 → 행 블록(ChartModel, 순수 관리 코드): 선곡 화면에서 곡에 커서가 가면 백그라운드에서 미리 돌리고(Prefetch)
    ///     결과를 (경로·크기·수정 시각·오프셋) 키로 캐시한다. 곡을 다시 하거나 재시작해도 파일이 그대로면 다시 파싱하지 않는다.
    ///  2) 행 블록 → Il2Cpp Step_t(BuildStep): Il2Cpp 객체 생성이라 메인 스레드에서만, 매번 새로 만든다
    ///     (게임이 판정 중 노트 객체를 다룰 수 있어 이전 판의 객체를 재사용하지 않는다).
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

        // Note의 dw(@0x10, uint)는 mAttr(+0) · mVisualEffect(+1) · mBank_Param(+2..3)을 겹쳐 쓴 공용체다.
        // 세 필드를 따로 쓰면 interop 필드 접근이 노트마다 3번 일어나므로 dw 한 번으로 같은 바이트를 쓴다(리틀엔디언).
        private static uint PackNote(byte attr) => attr | ((uint)DEFAULT_VISUAL_EFFECT << 8);
        private static readonly uint DW_EMPTY = PackNote(ATTR_EMPTY);
        private static readonly uint DW_TAP = PackNote(ATTR_TAP);
        private static readonly uint DW_HOLD_HEAD = PackNote(ATTR_HOLD_HEAD);
        private static readonly uint DW_HOLD_BODY = PackNote(ATTR_HOLD_BODY);
        private static readonly uint DW_HOLD_TAIL = PackNote(ATTR_HOLD_TAIL);

        // ===== 파싱 결과 캐시 =====
        private const int MaxCachedCharts = 8;
        private static readonly object _cacheLock = new();
        private static readonly Dictionary<string, Task<ParsedChart>> _cache = new(StringComparer.OrdinalIgnoreCase);

        private static string CacheKey(string path, float extraOffsetMs, float minDurationSeconds)
        {
            var fi = new FileInfo(path);
            return string.Concat(fi.FullName, "|", fi.Length.ToString(CultureInfo.InvariantCulture), "|",
                fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture), "|",
                extraOffsetMs.ToString("R", CultureInfo.InvariantCulture), "|",
                minDurationSeconds.ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// 캐시에 있으면 그 작업을, 없으면 새 파싱 작업을 돌려준다. background=true면 스레드 풀에서, 아니면 이 자리에서 돌린다.
        /// </summary>
        private static Task<ParsedChart> GetOrStartParse(string path, float extraOffsetMs, float minDurationSeconds, bool background, out bool reused)
        {
            string key = CacheKey(path, extraOffsetMs, minDurationSeconds);
            Task<ParsedChart> task;
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(key, out task) && !task.IsFaulted && !task.IsCanceled)
                {
                    reused = true;
                    return task;
                }
                if (_cache.Count >= MaxCachedCharts)
                    _cache.Clear();
                task = new Task<ParsedChart>(() => ChartModel.Parse(path, extraOffsetMs, minDurationSeconds));
                _cache[key] = task;
            }

            reused = false;
            if (background)
                task.Start(TaskScheduler.Default);
            else
                task.RunSynchronously(TaskScheduler.Default);
            return task;
        }

        /// <summary>
        /// 선곡 화면에서 커서가 곡에 올라갔을 때 부른다. 본게임 로딩 때는 캐시된 결과만 Il2Cpp로 옮기면 된다.
        /// 실패는 조용히 무시한다(본 로딩에서 다시 시도하며, 그때 오류를 로그로 남긴다).
        /// </summary>
        public static void Prefetch(string path, float extraOffsetMs, float minDurationSeconds = 0f)
        {
            if (string.IsNullOrEmpty(path))
                return;
            try { GetOrStartParse(path, extraOffsetMs, minDurationSeconds, background: true, out _); }
            catch { }
        }

        public static Step_t LoadFromFile(string path, float extraOffsetMs, float minDurationSeconds = 0f)
        {
            var total = Stopwatch.StartNew();
            var task = GetOrStartParse(path, extraOffsetMs, minDurationSeconds, background: false, out bool reused);
            bool wasRunning = reused && !task.IsCompleted;
            ParsedChart chart = task.GetAwaiter().GetResult(); // 파싱 예외는 원래 예외 그대로 호출자에게 간다
            double waitMs = total.Elapsed.TotalMilliseconds;

            // 백그라운드에서 모아 둔 파서 로그는 처음 쓸 때 한 번만 메인 스레드에서 출력한다
            lock (chart.Logs)
            {
                foreach (var (warn, text) in chart.Logs)
                {
                    if (warn) Mod.Log?.Warning(text);
                    else Mod.Log?.Msg(text);
                }
                chart.Logs.Clear();
            }

            var build = Stopwatch.StartNew();
            Step_t step = BuildStep(chart, out int noteObjects);
            double buildMs = build.Elapsed.TotalMilliseconds;

            string source = !reused ? $"즉시 파싱 {chart.ParseMs:0.0}ms"
                : wasRunning ? $"미리 읽던 파싱 대기 {waitMs:0.0}ms"
                : $"캐시 사용(파싱 {chart.ParseMs:0.0}ms는 이미 끝남)";
            Mod.Log?.Msg($"[ChartPerf] '{Path.GetFileName(path)}' {source}, Il2Cpp 구성 {buildMs:0.0}ms (행 {chart.TotalRows}, 노트 객체 {noteObjects}), 합계 {total.Elapsed.TotalMilliseconds:0.0}ms");
            return step;
        }

        /// <summary>
        /// 차트 파일이 없는 뼈대 테스트 폴더용 빈 Step_t 생성 (노트 0개, BGM/BGA 테스트용).
        /// </summary>
        public static Step_t CreateEmptyStep(int laneCount = 5, float bpm = 120f, float durationSeconds = 300f)
        {
            return BuildStep(ChartModel.CreateEmpty(laneCount, bpm, durationSeconds), out _);
        }

        private static uint PackedFor(char ch)
        {
            switch (ch)
            {
                case 'X': case 'x': return DW_TAP;
                case 'M': case 'm': return DW_HOLD_HEAD;
                case 'H': case 'h': return DW_HOLD_BODY;
                case 'W': case 'w': return DW_HOLD_TAIL;
                default: return DW_EMPTY;
            }
        }

        private static Step_t BuildStep(ParsedChart chart, out int noteObjects)
        {
            int laneCount = chart.LaneCount;
            var blocks = chart.Blocks;
            noteObjects = 0;

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
                        // mExtra(+0x14)는 새 객체에서 이미 0이다. dw 한 번으로 mAttr·mVisualEffect·mBank_Param을 쓴다.
                        var note = new Note();
                        note.dw = PackedFor(c < rowStr.Length ? rowStr[c] : '.');
                        noteArr[c] = note;
                    }
                    noteObjects += laneCount;
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
