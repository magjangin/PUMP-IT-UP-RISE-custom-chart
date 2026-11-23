# PROGRESS — PUMP IT UP RISE (Unity IL2CPP)

형식: `[완료|진행중|막힘] 단계명 — 산출물 경로 — 메모`

## 환경 (실측, 2026-11-22)
- 게임: H:\steam\steamapps\common\PUMP IT UP RISE (Steam appid 2756930, buildid 26399668, AutoUpdateBehavior 0 = 자동 업데이트 켜짐 → 버전 고정 안 됨)
- Unity 2022.3.62f2, IL2CPP, global-metadata.dat v31 (매직 AF1BB1FA, 암호화 없음), GameAssembly.dll 76.8MB
- 로더: MelonLoader 0.7.3 (version.dll 프록시). Mods/ 비어 있음, MelonLoader/Latest.log 없음 → 한 번도 실행되지 않음, Il2CppAssemblies 미생성
- 개발사: ANDAMIRO

## 진행
- [완료] 0. 작업 폴더 생성 — H:\source\repos\PUMP IT UP RISE-unity-il2cpp — il2cpp_dump/logs/docs/slices/input/mod/src_dummy
- [완료] 1. IL2CPP 메타데이터 덤프 — il2cpp_dump\ (DummyDll 95개, dump.cs 36MB, script.json 109MB, il2cpp.h, stringliteral.json) — Il2CppDumper 성공(v31, CodeRegistration 0x1837476e0, MetadataRegistration 0x183ca4c30). 로그 logs\il2cppdumper.log. Cpp2IL 재시도 불필요
- [완료] 2. 시그니처 탐색 — src_dummy\ (773개 .cs, Assembly-CSharp.csproj), il2cpp_dump\assembly-csharp.dump.cs (dump.cs 중 Assembly-CSharp 구간만 분리, TypeDefIndex 3416~16818) — 첫 시도는 Git Bash `cmd //c` 인용 문제로 실패(EXIT 64, 경로가 공백에서 잘림) → PowerShell `& ilspycmd.CMD`로 재실행해 성공. 로그 logs\ilspycmd.log
- [완료] 3. 네이티브 로직 분석 — slices\PUMPPlayer_judge.dll(124KB, JudgeNote/JudgeStep_PostProcess/GetScore/MainStage.SaveResult), logs\*.rea.c, docs\hooks.md — REA open_binary는 provider 자동선택 실패(ghidra/ida 모호) → provider_id "ghidra" 지정해 성공. pe_slice의 --method는 네임스페이스 포함 이름 필요("PIUMobileStepDLL.PUMPPlayer::JudgeNote")
- [완료] 4a. interop 어셈블리 생성 — H:\steam\steamapps\common\PUMP IT UP RISE\MelonLoader\Il2CppAssemblies\ (124개) — 게임 1차 실행(모드 0개)으로 MelonLoader가 생성. RemoteAPI에 게임 미등록(정상), Cpp2IL 2022.1.0-pre-release.21 / Il2CppInterop 1.5.1 / UnityDependencies 2022.3.62 자동 다운로드. "Assembly Generation Successful!" 로그 logs\melonloader_run1_generate.log
- [완료] 4b. 모드 스켈레톤 빌드 — mod\PIURiseSkeleton\ (PIURiseSkeleton.csproj, Mod.cs) → bin\Release\PIURiseSkeleton.dll — net6.0, 경고 0 오류 0. interop 네임스페이스: 전역 타입은 `Il2Cpp.*`(Il2Cpp.MainStage), 네임스페이스 타입은 `Il2Cpp` 접두(Il2CppPIUMobileStepDLL.PUMPPlayer). private 메서드도 interop에선 public이라 nameof 사용 가능. 로그 logs\dotnet_build.log
- [완료] 5. 실행 검증(로드) — logs\melonloader_run2_modload.log — `-p:Deploy=true`로 Mods\에 복사 후 Steam 실행. "1 Mod loaded.", "[PIURiseSkeleton] PIURiseSkeleton loaded ...", "Scene loaded: [0] PIUGame", error/exception 0건
- [완료] 5b. 후킹 발화 검증 — 2026-11-22 PIURiseCustom 실기 1판에서 MainStage.Start / SaveResult 로그·결과 화면 진입 확인 (AGENTS.md §7)
- [완료] 6. 인수인계 문서 — AGENTS.md, CLAUDE.md(@AGENTS.md) — 전역 규칙에 따라 생성
- [완료] 7. 오프라인 가능성 검토(정적) — docs\offline_feasibility.md, slices\SteamManager_auth.dll, logs\SteamManager_auth.rea.c — 결론: 가능(곡 플레이+로컬 기록 수준). 부팅 관문은 SteamManager.CheckTicket(checkTicketAndAuth)이고, 실패 시 오류 팝업 또는 MaintenanceManager.AuthError. 서버 REST https://riselive.piugame.com:3000/, WS :3011. 세이브는 Steam Cloud remote 폴더(로컬). 서버 원본 데이터(인벤·장비·시즌패스 등)는 응답 형식 캡처가 필요. DLC·Steam DRM 우회는 범위 밖
- [완료] 8. 커스텀 차트·BGM·BGA 파이프라인 정적 분석 — slices\custom_pipeline.dll, logs\MainStage.LoadAudioAndVideo.raw.c, AudioManager.LoadMusicSound.raw.c, BGAPlayer.Load.raw.c, BGAPlayer.DecryptCoroutine.raw.c, PlayBase.LoadStep.MoveNext.raw.c, StepLoader.LoadUserStep.raw.c — 핵심 발견: (1) BGM은 FMOD `MusicPlayer.Load(byte[])`로 일반 오디오(OGG/MP3/WAV/FLAC) 직접 로드 가능 (2) BGA는 `VideoMemoryServer.SetVideo(byte[], mime)` + Unity `VideoPlayer`로 MP4/WebM 직접 재생 가능 (3) 차트는 `PIUMobileStepDLL.Step.Load(byte[])`가 진입점이며 내장 `StepLoader.LoadUserStep`은 표준 UCS 포맷이나 멀티블록(변속) 버그가 있어 자체 멀티블록 UCS + 5/10키 BMS 파서로 `Step_t` 직접 구성
- [완료] 9. 통합 모드 프로토타입 구현·빌드·배포 — mod\PIURiseCustom\ (PIURiseCustom.csproj, Mod.cs, ModConfig.cs, CustomPackManager.cs, CustomChartParsers.cs, CustomMediaPatches.cs, SongTableInjectorPatches.cs, OfflineStubPatches.cs) — DEFLATE/STARGAZER 표준 `hwa/` 폴더 규격(루트/`hwa/<곡>/`/`hwa/<앨범>/<곡>/`, `info.txt`, BMS 키음 `.wav` 제외, 자켓/프리뷰) + `PIUDataBase.songTable` 독립 곡(`HWA_0001`...) 주입 + 자켓 스프라이트 교체까지 반영. 경고 0 오류 0 빌드 성공, `Mods\PIURiseCustom.dll` 배포 완료 (`PIURiseSkeleton.dll`은 훅 중복 방지로 제거)
- [완료] 9b. 실기 검증 (2026-11-22, v0.3.0) — 오프라인 부팅 → CUSTOM 탭 → 프리뷰 → `.bms` 1판 → 결과 화면까지 정상. 서버 응답 캡처는 미실시
- [완료] 10. 전체 감사·업데이트 내성·병목 개선 (2026-11-23, v0.3.1) — mod\PIURiseCustom\SafePatcher.cs, Util.cs 신규 외 전 파일
  - 업데이트 내성: `HarmonyDontPatchAll` + 패치 클래스별 격리 적용(61개), 부팅 시 Steam buildid·게임 버전 대조 경고, 관문 Prefix try/catch(`CacheSongTable`, `LoadStep`, 채널 목록 등), `Song.ID` 널 안전 헬퍼, hwa 스캔 폴더별 예외 격리, csproj `CheckGameReferences`(interop 미생성 시 즉시 원인 안내)
  - 병목: 자켓 캐시를 `File.Exists`보다 먼저 확인 + 디코딩 실패 이미지 재시도 차단, 오디오 바이트 MRU 2개 캐시, 키뷰어 키 테이블 프레임당 1회 조회·상태 변화 시에만 색 갱신, 곡 폴더 디렉터리 목록 1회 읽기, BMS 분할 자동 선택(실곡 4800→1200행)
  - 정확성: BMS 셋잇단 양자화, 캡처 JSONL 이스케이프, UTF-8/CP949/Shift-JIS 자동 판별
  - 청소: 미사용 `KeyInput.Awake` 훅·`TryGetAudioDurationSeconds`·`PackCount`·`IsMarathonBannerVisible`·`ClearJacketCache` 제거, 존재하지 않던 F9/F10 단축키 문서 삭제
  - 검증: 빌드 경고 0/오류 0, 실기 부팅 `[Patch] 61/61` + 오프라인 타이틀 진입 확인. 선곡~플레이 재실기는 다른 세션이 화면 제어 중이라 미실시

- [완료] 11. v0.3.2 오프라인 빈틈 보강 (2026-11-23, 실기 확인) — OfflineStubPatches.cs(멀티 접속 대상을 `NetworkManager.InitMultiCon`에서 `127.0.0.1:1`로 교체 — 1차 `ConnectAsync` 직접 후킹은 struct 인자 박싱으로 크래시, 2차 `Task.Delay(-1)`은 UI 대기, 방 생성 창 ESC/ENTER 먹통의 진짜 원인은 오프라인에서 `PlayerData.DLCEntrys`가 null → `EnsureOfflineDlcEntries`로 해결; 미확인 NetCommand 1회 경고, set 계열 명시), SongTableInjectorPatches.cs(방 생성 창 `SetSongInit` 템플릿 곡 치환 + 제목·자켓 복원), NoteVisualPatches.cs(풀링 방어·딕셔너리 통합), GameplayOptionPatches.cs(MaxScore를 `NumberRoller.RollCombo(Score)`에서 스케일), Mod.cs(0.3.2) — 외부 주소 전수 검색 결과 `riselive` REST·WS뿐. 빌드 경고 0/오류 0. 진단용 `MultiDiagPatches.cs`(16개)는 원인 해결 후 제거

- [수정·빌드만] 11b. MaxScore 재설계 (2026-11-23) — GameplayOptionPatches.cs — 실기에서 `MaxScore=2000000`이 적용되지 않는 것을 발견. 원인: 쓰던 `CalcNewBestScore` 계열은 점수 계산이 아니라 신기록 비교(REA 의사코드로 확인). `GetScoreAndAccuracy` 1차 시도는 실기에서 호출되지 않아 실패 → 로그로 확인된 실제 경로 `NumberRoller.RollCombo(Score, int)` Prefix로 재교체(패치 수 62 유지, 로그 전용 훅 2개). **실기 확인 완료 (16:39)**: 결과 화면 SCORE 2000000, 로그 `1000000 → 2000000`

- [수정·빌드] 12. v0.3.3 핫패스 정리 (2026-11-23) — NoteVisualPatches.cs(LateUpdate 훅 2개를 속성 기반 → 기능이 켜졌을 때만 `MainStage.Start`에서 동적 부착, 노트당 `Mathf.*`/`Time.time`/`Camera.main`/`Screen.height` IL2CPP 호출을 `MathF`·프레임 캐시로 대체), Mod.cs(`FrameIndex`, `PatchHarmony`, 0.3.3), OfflineStubPatches.cs(`Send` 차단 로그 명령별 1회), CHANGELOG.md 신설. 실기 미확인: 비주얼 기능 켬/끔 전환 시 훅 부착·해제
- [수정·빌드] 13. v0.3.4 전체 감사 (2026-11-23) — 전 파일 — 버그: 마라톤 전환 플래그 고착(공식 곡 결과창 ESC 막힘), 프리뷰/인게임 BGA `VideoPlayer` 핸들러 누적. 내성: 오프라인 패치 실패 오류 승격·기능 그룹 집계(SafePatcher), `Mod.OnUpdate` 서브시스템 격리, GAMESTART·IngameSongInfoUI.OnShow Prefix 격리, 노트 비주얼 훅 반쯤 붙음 롤백, 스켈레톤 `CheckGameReferences`. 성능: 판정바 틱 활성 상태 관리 쪽 기억, 모양 스프라이트 `SetPixels32` 일괄 생성, 오버레이·마라톤 꺼짐 시 조기 반환. 문서: hooks.md 플레이 옵션 행의 제거된 훅(`CalcNewBestScore`·`ScoreRoller.InitData`) 정정, 방 생성 창 주석의 원인 오기 정정. 빌드 경고 0/오류 0, 실기 미확인
- [통합·빌드] 14. v0.3.5 (2026-11-23) — 클라우드 감사 초안 PR #9(같은 날 v0.3.4로 작성, 미빌드)를 릴리즈된 v0.3.4 위에 병합. 중복(프리뷰 핸들러·오버레이 Math 치환)은 v0.3.4 쪽 유지, 고유분(BMS 03/08 토큰 길이·탭 헤더·끝 없는 홀드·LNTYPE 1·`#LNOBJ`, `[OnlineGuard]`, config 부팅 전용·읽기 성공 후 시각 기록, `_catalogIds`, `_pendingCustomPack` 1회 사용, `GetScoreAndAccuracy` 훅 제거로 60개, 반환값 Postfix·MainStage.Start 단계 격리, `tools/release.ps1`)은 채택. 빌드 경고 0/오류 0, 실기 미확인
- [완료·부팅 실기] 15. v0.3.6 (2026-11-24) — SafePatcher(사전 JIT 점검: 패치 클래스는 실패 시 미적용, 도우미는 경고), CustomPackManager(`SongIdRegistry` → `SaveCustomKey/song_ids.txt`), SaveCustomConfig(템플릿 기본값 깔기·모르는 키 알림), CustomChartParsers(`#LNOBJ` 짝 없는 끝 무시), GameplayOptionPatches(AutoPlay 키음 곡당 1회 조회), README 문제 해결 표. 실기 부팅: 60/60, JIT 352개 통과, ID 990001 유지

## 다음 세션 할 일
4c. v0.3.6/v0.3.5 실기: 부팅 `[Patch] 60/60` → 커스텀 곡 1판. v0.3.4분(판정바 모양 1/2, 선곡 프리뷰 영상 여러 번 오가기, 마라톤 곡 도중 이탈 → 공식 곡 결과창 ESC)과 v0.3.5분(LNTYPE 1/`#LNOBJ` 롱노트, 3자리 `#WAV` 차트 BPM, `OfflineMode=0`의 `[OnlineGuard]`) 항목은 AGENTS.md §7
0. v0.3.2 남은 실기: 인게임 HUD 점수 유무, 방 생성 창의 커스텀 곡 제목·자켓 표시, 멀티 방 목록 새로고침 등 나머지 조작. (`Patches=63`·멀티 메뉴 진입·방 생성/입장/빠른입장 차단·MaxScore는 확인 완료)
0b. ~~Perfect 수 744→417 차이 조사~~ **완료 (2026-11-23)**: 노트 누락 아님(탭 328+홀드 헤드 15=343 보존, 격자 밖·중복 0). 차이는 홀드 몸통 틱 판정이 Split에 따라 달라지기 때문. 홀드 틱의 정확한 공식은 미확인(AGENTS.md §7)
1. v0.3.2로 커스텀 곡 1판(선곡 → 플레이 → 결과) 재실기 (v0.3.1 로그는 선곡·프리뷰까지만 있고 플레이 기록 없음) — BMS Split=4 판정·스크롤, 프리뷰→본게임 오디오 캐시, 키뷰어 확인
2. `.ucs` 멀티블록(변속) 차트 실기 검증
3. ~~점수표 상수 확인~~ **완료 (2026-11-23)** — dump.cs에 적힌 `Metadata offset`(0x97E3F0 / 0x97E570)을 global-metadata.dat에서 직접 읽음. int[6] `21A1F6B9...` = {1000, 1000, 500, 100, -200, -500}, float[5] `9EC6817D...` = {1, 0.7, 0.3, 0.1, 0}. Cpp2IL·런타임 불필요
4. 버전 고정 여부 결정: 현재 AutoUpdateBehavior 0(자동 업데이트). 업데이트되면 docs/hooks.md §5 절차를 따를 것
4b. v0.3.3 실기: `NoteSway=1`로 곡 진입 → `[NoteVisual] … 붙였습니다` + 흔들림 정상, 다시 `NoteSway=0` → `… 뗐습니다` + 노트 정상
5. (선택) `NoteVisualPatches` 풀링 방어·`MaxScore` 중복 방지는 v0.3.2에서 코드로 넣었으니 실기로 효과 확인 (AGENTS.md §7)
6. (선택) `float[5]`(1, 0.7, 0.3, 0.1, 0)가 쓰이는 곳(`_scoreJudge` @0x164 호출부) 추적
