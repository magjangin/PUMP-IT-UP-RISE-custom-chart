using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PIURiseCustom
{
    internal sealed class CustomPack
    {
        // 선곡 목록 주입용 ID: "990001"부터 스캔 순서(경로 사전순)대로 부여.
        // 폴더를 추가/삭제하면 뒤쪽 곡 ID가 밀린다 — 게임 즐겨찾기 등 ID 기반 로컬 데이터가 다른 곡을 가리킬 수 있음.
        public string SongId;
        public string FolderPath;
        public string FolderName;
        public string AlbumName;     // 2단 깊이일 때 상위 앨범 폴더명 (없으면 null)

        public string Title;
        public string Artist;
        public string BpmText;
        public int SingleLevel;      // 기본 10 (대표 레벨)
        public int DoubleLevel;      // 더블 차트면 > 0, 아니면 0
        public List<int> SingleLevels = new(); // 다중 싱글 레벨 (예: 5,5,5,5,5)
        public List<int> DoubleLevels = new(); // 다중 더블 레벨 (미지정 시 SingleLevels 폴백)

        public string ChartPath;       // .ucs / .bms / .bme / .bml
        public string BgmPath;         // .ogg / .mp3 / .wav / .flac
        public string PreviewBgmPath;  // music_preview.* 등 (없으면 BgmPath 폴백)
        public string BgaPath;         // .mp4 / .webm
        public string JacketPath;           // cover.png / jacket.png / Thumbnail.png 등
        public string LoadingThumbnailPath; // 해당 폴더의 첫 번째 16:9 비율 이미지 (로딩 씬 썸네일)
        public string InfoPath;             // info.txt 등

        public bool IsOverrideSlot => FolderName.Equals("override", StringComparison.OrdinalIgnoreCase);
        public bool HasPlayableMedia => ChartPath != null || BgmPath != null || BgaPath != null || JacketPath != null;
    }

    /// <summary>
    /// 기존 DEFLATE / STARGAZER 모드와 동일한 hwa/ 폴더 규격 스캐너.
    /// 지원 배치:
    ///   1) hwa/ 루트 직접 배치 (곡 1개)
    ///   2) hwa/<곡 폴더>/ (1단계 하위폴더 = 곡 1개)
    ///   3) hwa/<앨범 폴더>/<곡 폴더>/ (2단계 하위폴더 = 앨범 내 곡들)
    ///   + hwa/override/ 또는 hwa/<공식SongID>/ 는 공식 곡 덮어쓰기 슬롯으로도 동작.
    /// </summary>
    internal static class CustomPackManager
    {
        private static readonly string[] ChartExts = { ".ucs", ".bms", ".bme", ".bml" };
        private static readonly string[] AudioExts = { ".ogg", ".mp3", ".wav", ".flac" };
        private static readonly string[] VideoExts = { ".mp4", ".webm" };
        private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg" };

        private static readonly string[] BgmPreferredNames = { "music", "bgm", "audio", "song", "track" };
        private static readonly string[] PreviewPreferredNames = { "music_preview", "preview", "bgm_preview" };
        private static readonly string[] JacketPreferredNames =
            { "jacket", "cover", "thumbnail", "자켓", "커버", "썸네일", "封面", "封套", "曲绘", "插图" };

        private static readonly List<CustomPack> _catalogSongs = new();
        private static readonly Dictionary<string, CustomPack> _bySongId = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, CustomPack> _byFolderKey = new(StringComparer.OrdinalIgnoreCase);
        // 선곡 목록에 실제로 주입한 곡 ID만. override 슬롯 팩도 ID는 받지만(번호 유지) 목록에는 없으므로 여기서 뺀다.
        private static readonly HashSet<string> _catalogIds = new(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<CustomPack> CatalogSongs => _catalogSongs;

        public static void EnsureSampleFoldersAndScan()
        {
            string root = ModConfig.ResolvedCustomRoot;
            try
            {
                if (!Directory.Exists(root))
                {
                    Directory.CreateDirectory(root);
                }
            }
            catch (Exception ex)
            {
                Mod.Log?.Warning($"[hwa] 폴더 생성 중 오류: {ex.Message}");
            }

            Scan();
        }

        public static void Scan()
        {
            _catalogSongs.Clear();
            _bySongId.Clear();
            _byFolderKey.Clear();
            _catalogIds.Clear();

            string root = ModConfig.ResolvedCustomRoot;
            if (!Directory.Exists(root))
                return;

            int failed = 0;
            var ids = SongIdRegistry.Load(root);

            // 폴더 하나가 깨져 있어도(권한·잠금·이상한 파일) 나머지 곡은 계속 등록한다.
            // 여기서 예외가 새면 OnInitializeMelon이 실패해 모드 전체가 꺼진다.
            void TryAdd(string dir, string folderName, string albumName, string[] files, bool catalogUnlessOverride)
            {
                try
                {
                    string relKey = SongIdRegistry.RelativeKey(root, dir);
                    var pack = BuildPack(dir, folderName, albumName, files, () => ids.GetOrAssign(relKey));
                    RegisterPack(pack, isCatalogSong: !catalogUnlessOverride || !pack.IsOverrideSlot);
                }
                catch (Exception ex)
                {
                    failed++;
                    Mod.Log?.Warning($"[hwa] 곡 폴더 건너뜀 ({dir}): {ex.Message}");
                }
            }

            // 1) hwa/ 루트에 직접 곡 파일이 배치된 경우 (구방식)
            var rootFiles = GetFilesSorted(root);
            if (IsSongFolder(rootFiles))
                TryAdd(root, "hwa", null, rootFiles, catalogUnlessOverride: false);

            // 2) 1단계 하위 폴더 스캔 (곡 폴더 또는 앨범 폴더 자동 판별)
            foreach (string dir in GetDirectoriesSorted(root))
            {
                string dirName = Path.GetFileName(dir);
                var files = GetFilesSorted(dir);

                if (IsSongFolder(files))
                {
                    TryAdd(dir, dirName, null, files, catalogUnlessOverride: true);
                    continue;
                }

                // 하위 폴더에 곡이 있으면 앨범 폴더로 간주
                foreach (string subDir in GetDirectoriesSorted(dir))
                {
                    var subFiles = GetFilesSorted(subDir);
                    if (IsSongFolder(subFiles))
                        TryAdd(subDir, Path.GetFileName(subDir), dirName, subFiles, catalogUnlessOverride: false);
                }
            }

            if (failed > 0)
                Mod.Log?.Warning($"[hwa] 읽기 실패로 건너뛴 곡 폴더 {failed}개");

            ids.SaveIfChanged();

            Mod.Log?.Msg($"[hwa] 스캔 완료: {root} — 목록 주입 곡 {_catalogSongs.Count}개, 전체 등록 팩 {_byFolderKey.Count}개");
        }

        private static void RegisterPack(CustomPack pack, bool isCatalogSong)
        {
            if (pack == null || !pack.HasPlayableMedia)
                return;

            _bySongId[pack.SongId] = pack;
            _byFolderKey[pack.FolderName] = pack;
            if (!string.IsNullOrEmpty(pack.AlbumName))
                _byFolderKey[$"{pack.AlbumName}/{pack.FolderName}"] = pack;

            if (isCatalogSong)
            {
                _catalogSongs.Add(pack);
                _catalogIds.Add(pack.SongId);
            }

            string albumTag = string.IsNullOrEmpty(pack.AlbumName) ? "" : $"[{pack.AlbumName}] ";
            Mod.Log?.Msg(
                $"[hwa] {pack.SongId} {albumTag}'{pack.Title}' by {pack.Artist} (BPM {pack.BpmText}) — " +
                $"Chart={NameOrNone(pack.ChartPath)}, BGM={NameOrNone(pack.BgmPath)}, " +
                $"BGA={NameOrNone(pack.BgaPath)}, Jacket={NameOrNone(pack.JacketPath)}");
        }

        /// <summary>
        /// DEFLATE/STARGAZER 규격: 차트(.ucs/.bms/.bme/.bml), info.txt(readme 제외 .txt), .png/.jpg, .mp4/.webm,
        /// 또는 지정 이름의 음원(.ogg/.mp3)이 직접 존재할 때만 곡 폴더로 인정 (.wav 키음 폴더 제외).
        /// </summary>
        private static string[] GetFilesSorted(string dir)
        {
            try
            {
                var files = Directory.GetFiles(dir);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                return files;
            }
            catch (Exception ex)
            {
                Mod.Log?.Warning($"[hwa] 파일 목록 읽기 실패 ({dir}): {ex.Message}");
                return Array.Empty<string>();
            }
        }

        private static string[] GetDirectoriesSorted(string dir)
        {
            try
            {
                var dirs = Directory.GetDirectories(dir);
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                return dirs;
            }
            catch (Exception ex)
            {
                Mod.Log?.Warning($"[hwa] 하위 폴더 목록 읽기 실패 ({dir}): {ex.Message}");
                return Array.Empty<string>();
            }
        }

        private static bool IsSongFolder(string[] files)
        {
            foreach (string f in files)
            {
                string name = Path.GetFileName(f);
                string ext = Path.GetExtension(f)?.ToLowerInvariant();
                if (ChartExts.Contains(ext)) return true;
                if (VideoExts.Contains(ext)) return true;
                if (ImageExts.Contains(ext)) return true;
                if (ext == ".ogg" || ext == ".mp3" || ext == ".flac") return true;
                if (ext == ".txt" && !name.Equals("readme.txt", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // 디렉터리 목록은 Scan에서 한 번만 읽어 넘긴다 (예전엔 IsSongFolder/BuildPack이 폴더마다 두 번 읽었음)
        private static CustomPack BuildPack(string dir, string folderName, string albumName, string[] files, Func<string> nextSongId)
        {
            string chartPath = FindFirstByExt(files, ChartExts);
            string bgmPath = FindBgm(files);
            string previewPath = FindPreviewBgm(files);
            string bgaPath = FindFirstByExt(files, VideoExts);
            string jacketPath = FindJacket(files);
            string loadingThumbPath = FindLoadingThumbnail(files);
            string infoPath = FindInfoTxt(files);

            var pack = new CustomPack
            {
                SongId = nextSongId(),
                FolderPath = dir,
                FolderName = folderName,
                AlbumName = albumName,
                ChartPath = chartPath,
                BgmPath = bgmPath,
                PreviewBgmPath = previewPath,
                BgaPath = bgaPath,
                JacketPath = jacketPath,
                LoadingThumbnailPath = loadingThumbPath,
                InfoPath = infoPath,
                Title = folderName,
                Artist = "화영왕",
                BpmText = "120",
                SingleLevel = 10,
                DoubleLevel = 0
            };

            // 1) BMS / UCS 헤더에서 기본 메타데이터 읽기
            if (chartPath != null)
                ReadChartHeaderMetadata(chartPath, pack);

            // 2) info.txt가 있으면 최우선 덮어쓰기
            if (infoPath != null)
                ReadInfoTxt(infoPath, pack);

            return pack;
        }

        private static string FindBgm(string[] files)
        {
            // 1순위: music / bgm / audio / song / track 이름 매칭 (확장자 ogg/mp3/wav/flac)
            foreach (string pref in BgmPreferredNames)
            {
                foreach (string ext in AudioExts)
                {
                    var match = files.FirstOrDefault(f =>
                        Path.GetFileNameWithoutExtension(f).Equals(pref, StringComparison.OrdinalIgnoreCase) &&
                        f.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return match;
                }
            }

            // 2순위: 프리뷰 제외하고 이름에 키워드가 들어간 파일 (ogg/mp3/flac 우선, wav 제외해 키음 오인식 방지)
            var audioNonWav = files.Where(f =>
            {
                string ext = Path.GetExtension(f)?.ToLowerInvariant();
                if (ext != ".ogg" && ext != ".mp3" && ext != ".flac") return false;
                string stem = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                return !stem.Contains("preview");
            }).ToArray();

            foreach (string pref in BgmPreferredNames)
            {
                var match = audioNonWav.FirstOrDefault(f =>
                    Path.GetFileNameWithoutExtension(f).IndexOf(pref, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null) return match;
            }

            // 3순위: 첫 번째 .ogg → .mp3 → .flac (용량 큰 파일 우선)
            var ogg = audioNonWav.Where(f => f.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
            if (ogg != null) return ogg;

            return audioNonWav.OrderByDescending(f => new FileInfo(f).Length).FirstOrDefault();
        }

        private static string FindPreviewBgm(string[] files)
        {
            foreach (string pref in PreviewPreferredNames)
            {
                foreach (string ext in AudioExts)
                {
                    var match = files.FirstOrDefault(f =>
                        Path.GetFileNameWithoutExtension(f).Equals(pref, StringComparison.OrdinalIgnoreCase) &&
                        f.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return match;
                }
            }
            return null;
        }

        private static string FindJacket(string[] files)
        {
            foreach (string pref in JacketPreferredNames)
            {
                foreach (string ext in ImageExts)
                {
                    var match = files.FirstOrDefault(f =>
                        Path.GetFileNameWithoutExtension(f).Equals(pref, StringComparison.OrdinalIgnoreCase) &&
                        f.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return match;
                }
            }

            // 폴백: bg.* 제외한 첫 번째 이미지
            return files.FirstOrDefault(f =>
            {
                string ext = Path.GetExtension(f)?.ToLowerInvariant();
                if (!ImageExts.Contains(ext)) return false;
                string stem = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                return stem != "bg" && !stem.StartsWith("bg_");
            });
        }

        /// <summary>
        /// 해당 곡 폴더의 이미지 중 맨 첫 번째 16:9 비율 이미지를 로딩 씬 썸네일로 선택한다.
        /// </summary>
        private static string FindLoadingThumbnail(string[] files)
        {
            const float target16By9 = 16f / 9f; // 1.7778
            const float tolerance = 0.15f;      // 1.63 ~ 1.93 (1920x1080, 1888x1056, 1280x720 등)

            foreach (string f in files)
            {
                string ext = Path.GetExtension(f)?.ToLowerInvariant();
                if (!ImageExts.Contains(ext)) continue;

                if (TryReadImageDimensions(f, out int w, out int h) && w > 0 && h > 0)
                {
                    float ratio = (float)w / h;
                    if (Math.Abs(ratio - target16By9) <= tolerance)
                        return f;
                }
            }
            return null;
        }

        private static bool TryReadImageDimensions(string path, out int width, out int height)
        {
            width = 0;
            height = 0;
            try
            {
                using var fs = File.OpenRead(path);
                if (fs.Length < 24) return false;

                byte[] header = new byte[24];
                fs.Read(header, 0, 24);

                // PNG: 89 50 4E 47 0D 0A 1A 0A, IHDR width@16..19 height@20..23 (big-endian)
                if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
                {
                    width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                    height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                    return width > 0 && height > 0;
                }

                // JPEG: FF D8 -> scan SOF0/SOF1/SOF2 markers
                if (header[0] == 0xFF && header[1] == 0xD8)
                {
                    fs.Position = 2;
                    while (fs.Position + 4 < fs.Length)
                    {
                        int b = fs.ReadByte();
                        if (b != 0xFF) continue;
                        int marker = fs.ReadByte();
                        while (marker == 0xFF) marker = fs.ReadByte();
                        if (marker < 0 || marker == 0xD9 || marker == 0xDA) break;

                        int lenHi = fs.ReadByte();
                        int lenLo = fs.ReadByte();
                        if (lenHi < 0 || lenLo < 0) break;
                        int segLen = (lenHi << 8) | lenLo;

                        if (marker == 0xC0 || marker == 0xC1 || marker == 0xC2)
                        {
                            fs.ReadByte(); // precision
                            int hHi = fs.ReadByte(); int hLo = fs.ReadByte();
                            int wHi = fs.ReadByte(); int wLo = fs.ReadByte();
                            if (wLo < 0) break;
                            height = (hHi << 8) | hLo;
                            width = (wHi << 8) | wLo;
                            return width > 0 && height > 0;
                        }
                        if (segLen < 2) break;
                        fs.Position += segLen - 2;
                    }
                }
            }
            catch { }
            return false;
        }

        private static string FindInfoTxt(string[] files)
        {
            var exact = files.FirstOrDefault(f =>
                Path.GetFileName(f).Equals("info.txt", StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            return files.FirstOrDefault(f =>
                f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(f).Equals("readme.txt", StringComparison.OrdinalIgnoreCase));
        }

        private static void ReadChartHeaderMetadata(string chartPath, CustomPack pack)
        {
            try
            {
                string ext = Path.GetExtension(chartPath)?.ToLowerInvariant();
                int lineCount = 0;
                foreach (string raw in Util.ReadAllLinesSmart(chartPath))
                {
                    if (++lineCount > 200) break;
                    string line = raw.Trim().Replace('\t', ' ');
                    if (ext == ".ucs")
                    {
                        if (line.StartsWith(":Mode=", StringComparison.OrdinalIgnoreCase))
                        {
                            string mode = line.Substring(6).Trim();
                            if (mode.IndexOf('D', StringComparison.OrdinalIgnoreCase) >= 0)
                                pack.DoubleLevel = Math.Max(pack.DoubleLevel, 10);
                        }
                        else if (line.StartsWith(":BPM=", StringComparison.OrdinalIgnoreCase))
                        {
                            pack.BpmText = line.Substring(5).Trim();
                        }
                    }
                    else
                    {
                        if (line.StartsWith("#TITLE ", StringComparison.OrdinalIgnoreCase))
                        {
                            string t = line.Substring(7).Trim();
                            if (!string.IsNullOrEmpty(t)) pack.Title = t;
                        }
                        else if (line.StartsWith("#ARTIST ", StringComparison.OrdinalIgnoreCase))
                        {
                            string a = line.Substring(8).Trim();
                            if (!string.IsNullOrEmpty(a)) pack.Artist = a;
                        }
                        else if (line.StartsWith("#BPM ", StringComparison.OrdinalIgnoreCase))
                        {
                            string b = line.Substring(5).Trim();
                            if (!string.IsNullOrEmpty(b)) pack.BpmText = b;
                        }
                        else if (line.StartsWith("#PLAYLEVEL ", StringComparison.OrdinalIgnoreCase))
                        {
                            if (int.TryParse(line.Substring(11).Trim(), out int lv) && lv > 0)
                                pack.SingleLevel = Math.Clamp(lv, 1, 28);
                        }
                    }
                }
            }
            catch { }
        }

        private static void ReadInfoTxt(string infoPath, CustomPack pack)
        {
            try
            {
                if (new FileInfo(infoPath).Length > 16 * 1024 * 1024)
                    return;

                foreach (string raw in Util.ReadAllLinesSmart(infoPath))
                {
                    string line = raw.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                    int sep = line.IndexOfAny(new[] { ':', '=' });
                    if (sep < 0) continue;

                    string key = line.Substring(0, sep).Trim().ToLowerInvariant();
                    string val = line.Substring(sep + 1).Trim();
                    if (string.IsNullOrEmpty(val)) continue;

                    switch (key)
                    {
                        case "title":
                        case "name":
                        case "곡 제목":
                        case "제목":
                        case "曲名":
                        case "歌名":
                        case "标题":
                            pack.Title = val;
                            break;
                        case "artist":
                        case "composer":
                        case "아티스트":
                        case "작곡":
                        case "艺术家":
                        case "作曲":
                        case "曲师":
                            pack.Artist = val;
                            break;
                        case "bpm":
                            pack.BpmText = val;
                            break;
                        case "level":
                        case "levels":
                        case "single":
                        case "lv":
                        case "레벨":
                        case "난이도":
                        case "난이도 설정":
                            {
                                var parsed = ParseLevelList(val);
                                if (parsed.Count > 0)
                                {
                                    pack.SingleLevels = parsed;
                                    pack.SingleLevel = parsed[0];
                                }
                            }
                            break;
                        case "double":
                        case "더블":
                        case "더블 난이도":
                        case "더블 난이도 설정":
                            {
                                var parsed = ParseLevelList(val);
                                if (parsed.Count > 0)
                                {
                                    pack.DoubleLevels = parsed;
                                    pack.DoubleLevel = parsed[0];
                                }
                            }
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Mod.Log?.Warning($"[hwa] info.txt 읽기 실패 ({infoPath}): {ex.Message}");
            }
        }

        private static List<int> ParseLevelList(string raw)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(raw))
                return result;

            string[] tokens = raw.Split(new[] { ',', '/', '|', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string tok in tokens)
            {
                if (int.TryParse(tok.Trim(), out int lv) && lv > 0)
                    result.Add(Math.Clamp(lv, 1, 28));
            }
            return result;
        }

        private static string FindFirstByExt(string[] files, string[] exts)
        {
            foreach (string ext in exts)
            {
                var match = files.FirstOrDefault(f => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            return null;
        }

        private static string NameOrNone(string path) =>
            path == null ? "(없음)" : Path.GetFileName(path);

        public static bool IsCatalogSongId(string songId)
        {
            return !string.IsNullOrEmpty(songId) && _catalogIds.Contains(songId);
        }

        public static CustomPack ResolveBySongId(string songId)
        {
            if (!ModConfig.CustomEnabled.Value || string.IsNullOrEmpty(songId))
                return null;

            if (_bySongId.TryGetValue(songId, out var byId))
                return byId;

            if (_byFolderKey.TryGetValue(songId, out var byFolder))
                return byFolder;

            return null;
        }

        public static CustomPack Resolve(string songId, string playType = null, int level = 0)
        {
            if (!ModConfig.CustomEnabled.Value)
                return null;

            var direct = ResolveBySongId(songId);
            if (direct != null)
                return direct;

            // hwa/override/ 폴더가 있으면 공식 곡 선택 시에도 덮어쓰기 슬롯으로 동작
            if (_byFolderKey.TryGetValue("override", out var globalOverride))
                return globalOverride;

            return null;
        }
    }
    /// <summary>
    /// 커스텀 곡 ID(990001…)를 곡 폴더별로 기억하는 표 — SaveCustomKey/song_ids.txt (v0.3.6).
    /// 예전엔 스캔할 때마다 경로 사전순으로 990001부터 다시 매겨서, hwa/에 폴더를 끼워 넣거나 지우면 뒤쪽 곡 ID가 전부 밀렸다
    /// (게임의 즐겨찾기 등 곡 ID 기반 로컬 데이터가 다른 곡을 가리킴). 이제 한 번 받은 ID는 폴더가 그대로인 한 바뀌지 않는다.
    /// - 표가 없는 첫 실행은 예전과 똑같이 스캔 순서대로 990001부터 매긴다(기존 설치와 같은 번호 → 호환).
    /// - 새 폴더는 지금까지 쓴 가장 큰 번호 다음을 받는다. 지운 폴더의 번호는 표에 남겨 두고 다시 쓰지 않는다.
    /// - 폴더 이름을 바꾸면 새 곡으로 취급되어 새 번호를 받는다.
    /// - 표를 읽거나 쓰지 못해도 스캔은 계속된다(그 실행에서는 번호가 저장되지 않을 뿐).
    /// </summary>
    internal sealed class SongIdRegistry
    {
        private const int BaseId = 990000;
        private static string FilePath => Path.Combine(SaveCustomConfig.ConfigDir, "song_ids.txt");

        private readonly Dictionary<string, int> _byKey = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<int> _usedThisScan = new();
        private int _maxId = BaseId;
        private bool _changed;

        public static string RelativeKey(string root, string dir)
        {
            string rel = Path.GetRelativePath(root, dir).Replace(Path.DirectorySeparatorChar, '/');
            return string.IsNullOrEmpty(rel) ? "." : rel;
        }

        public static SongIdRegistry Load(string root)
        {
            var reg = new SongIdRegistry();
            try
            {
                if (!File.Exists(FilePath))
                    return reg;
                foreach (string raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    if (!int.TryParse(line.Substring(0, eq).Trim(), out int id) || id <= BaseId) continue;
                    string key = line.Substring(eq + 1).Trim();
                    if (key.Length == 0 || reg._byKey.ContainsKey(key)) continue;
                    reg._byKey[key] = id;
                    if (id > reg._maxId) reg._maxId = id;
                }
            }
            catch (Exception ex)
            {
                Mod.Log?.Warning($"[hwa] 곡 ID 표 읽기 실패({FilePath}) — 이번 실행은 새로 매깁니다: {ex.Message}");
            }
            return reg;
        }

        public string GetOrAssign(string key)
        {
            // 손으로 고친 표에서 두 폴더가 같은 번호를 가리키면 뒤쪽 폴더에 새 번호를 준다
            if (_byKey.TryGetValue(key, out int id) && _usedThisScan.Add(id))
                return id.ToString();

            id = ++_maxId;
            _byKey[key] = id;
            _usedThisScan.Add(id);
            _changed = true;
            return id.ToString();
        }

        public void SaveIfChanged()
        {
            if (!_changed) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var sb = new StringBuilder();
                sb.AppendLine("# PIURiseCustom 커스텀 곡 ID 표 (자동 생성). 곡 ID=hwa 기준 폴더 경로");
                sb.AppendLine("# 지운 폴더의 번호도 남겨 두어 다시 쓰지 않습니다. 지우면 다음 실행 때 스캔 순서대로 다시 매깁니다.");
                foreach (var kv in _byKey.OrderBy(kv => kv.Value))
                    sb.Append(kv.Value).Append('=').AppendLine(kv.Key);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                File.Move(tmp, FilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Mod.Log?.Warning($"[hwa] 곡 ID 표 저장 실패({FilePath}) — 다음 실행 때 번호가 달라질 수 있습니다: {ex.Message}");
            }
        }
    }
}
