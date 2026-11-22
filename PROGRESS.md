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
- [진행중] 5b. 후킹 발화 검증 — 미확인. 곡을 한 판 플레이해야 MainStage.Start / SaveResult 로그와 판정 카운트가 찍힌다. 플레이 후 MelonLoader\Latest.log에서 "MainStage.SaveResult — Perfect ..." 줄 확인할 것

## 다음 세션 할 일
1. 곡 1판 플레이 → Latest.log에서 MainStage.Start / SaveResult 로그 확인 (판정 카운트가 결과 화면과 일치하는지)
2. 점수표 상수(int[6] `<PrivateImplementationDetails>.21A1F6B9...`, float[5] `9EC6817D...`) 값 확인 — il2cpp_dump\stringliteral.json이 아니라 metadata의 FieldRVA 데이터라 Il2CppDumper 산출물에 없음. Cpp2IL 또는 런타임에서 읽어야 함
3. JudgeUnit(0x79A210), CalcScore_PostPlaying(0x79C170) 조각 PE 분석
4. 버전 고정 여부 결정: 현재 AutoUpdateBehavior 0(자동 업데이트). 업데이트되면 RVA·interop 재생성 필요(MelonLoader가 자동 재생성)
- [완료] 6. 인수인계 문서 — AGENTS.md, CLAUDE.md(@AGENTS.md) — 전역 규칙에 따라 생성
- [완료] 7. 오프라인 가능성 검토(정적) — docs\offline_feasibility.md, slices\SteamManager_auth.dll, logs\SteamManager_auth.rea.c — 결론: 가능(곡 플레이+로컬 기록 수준). 부팅 관문은 SteamManager.CheckTicket(checkTicketAndAuth)이고, 실패 시 오류 팝업 또는 MaintenanceManager.AuthError. 서버 REST https://riselive.piugame.com:3000/, WS :3011. 세이브는 Steam Cloud remote 폴더(로컬). 서버 원본 데이터(인벤·장비·시즌패스 등)는 응답 형식 캡처가 필요. DLC·Steam DRM 우회는 범위 밖
- [완료] 8. 커스텀 차트·BGM·BGA 파이프라인 정적 분석 — slices\custom_pipeline.dll, logs\MainStage.LoadAudioAndVideo.raw.c, AudioManager.LoadMusicSound.raw.c, BGAPlayer.Load.raw.c, BGAPlayer.DecryptCoroutine.raw.c, PlayBase.LoadStep.MoveNext.raw.c, StepLoader.LoadUserStep.raw.c — 핵심 발견: (1) BGM은 FMOD `MusicPlayer.Load(byte[])`로 일반 오디오(OGG/MP3/WAV/FLAC) 직접 로드 가능 (2) BGA는 `VideoMemoryServer.SetVideo(byte[], mime)` + Unity `VideoPlayer`로 MP4/WebM 직접 재생 가능 (3) 차트는 `PIUMobileStepDLL.Step.Load(byte[])`가 진입점이며 내장 `StepLoader.LoadUserStep`은 표준 UCS 포맷이나 멀티블록(변속) 버그가 있어 자체 멀티블록 UCS + 5/10키 BMS 파서로 `Step_t` 직접 구성
- [완료] 9. 통합 모드 프로토타입 구현·빌드·배포 — mod\PIURiseCustom\ (PIURiseCustom.csproj, Mod.cs, ModConfig.cs, CustomPackManager.cs, CustomChartParsers.cs, CustomMediaPatches.cs, SongTableInjectorPatches.cs, OfflineStubPatches.cs) — DEFLATE/STARGAZER 표준 `hwa/` 폴더 규격(루트/`hwa/<곡>/`/`hwa/<앨범>/<곡>/`, `info.txt`, BMS 키음 `.wav` 제외, 자켓/프리뷰) + `PIUDataBase.songTable` 독립 곡(`HWA_0001`...) 주입 + 자켓 스프라이트 교체까지 반영. 경고 0 오류 0 빌드 성공, `Mods\PIURiseCustom.dll` 배포 완료 (`PIURiseSkeleton.dll`은 훅 중복 방지로 제거)
- [진행중] 9b. 실기 검증 — 게임 실행 후 (1) 오프라인 스텁으로 타이틀/선곡 진입 여부 확인 (2) `hwa/<곡 폴더>/`에 `.ucs`/`.bms` + BGM + BGA + 자켓 넣고 선곡 목록 표시 및 1판 플레이 검증 (3) 필요 시 `OfflineMode=false, CaptureOnlineResponses=true`로 서버 응답 JSON 형식 캡처 후 스텁 보강
