# PUMP IT UP RISE — 인수인계 문서 (AGENTS.md)

> 작성 2026-11-22. 단계별 진행 기록은 [PROGRESS.md](PROGRESS.md), 후킹 후보는 [docs/hooks.md](docs/hooks.md).
> **확인** = 디스크·로그·의사코드로 직접 본 것, **추정** = 해석.

## 1. 게임 버전 및 고정 여부
- Steam appid 2756930, **buildid 26399668**, 게임 버전 1.6.0 (MelonLoader 로그), GameAssembly.dll 수정일 2025-11-05 (확인)
- **고정 안 됨**: appmanifest_2756930.acf `AutoUpdateBehavior 0`(자동 업데이트). DepotDownloader 다운그레이드·.acf 패치 차단은 하지 않았다
- 업데이트 시 RVA(docs/hooks.md)는 전부 무효. Harmony 패치는 이름 기반이라 메서드 이름이 유지되면 동작, MelonLoader가 interop 자동 재생성

## 2. 런타임 백엔드 및 디컴파일 경로
- Unity 2022.3.62f2, **IL2CPP**, global-metadata.dat **v31**(암호화 없음, 매직 AF1BB1FA)
- 로더: MelonLoader 0.7.3 (version.dll 프록시, net6 런타임, 실제 호스트는 .NET 10 hostfxr)
- 덤프: `il2cpp_dump\` (Il2CppDumper, DummyDll/dump.cs/script.json/il2cpp.h). Assembly-CSharp만 분리: `il2cpp_dump\assembly-csharp.dump.cs`
- 시그니처: `src_dummy\` (ilspycmd, 본문 없음, RVA 주석)
- interop(모드 참조용): `<게임>\MelonLoader\Il2CppAssemblies\` (게임 첫 실행 때 MelonLoader가 생성)
- 의사코드: `slices\*.dll`(pe_slice) → REA(Ghidra) → `logs\*.rea.c`

## 3. 설계 의도와 우회 사유 (Rationale)
- 스켈레톤은 **읽기 전용 Postfix**만 건다.
- 판정 관찰 지점으로 `JudgeNote`가 아니라 `JudgeStep_PostProcess`를 택한 이유: 판정 결과(JUDGE)가 확정된 뒤 집계하는 단일 지점이라 롱노트·라인 판정까지 한곳에서 잡힌다(의사코드 확인)
- 모드 배포는 `-p:Deploy=true`일 때만 Mods\로 복사 — 기본 빌드는 게임 폴더를 건드리지 않게 하려는 것

## 4. 핵심 훅 시그니처와 주입 파이프라인
- 주입: version.dll(MelonLoader) → Mods\PIURiseSkeleton.dll → `HarmonyPatch` 자동 적용
- interop 네임스페이스: 전역 타입 `Il2Cpp.MainStage`, 네임스페이스 타입 `Il2CppPIUMobileStepDLL.PUMPPlayer` (확인)
- 훅:
  - `Il2Cpp.MainStage.Start()` RVA 0x67F5C0 — 스테이지 시작, 카운터 초기화
  - `Il2CppPIUMobileStepDLL.PUMPPlayer.JudgeStep_PostProcess(int bank, JUDGE judgement, int noteCount, bool bJudgeLong, float factor)` RVA 0x79A7B0 — 판정 집계
  - `Il2Cpp.MainStage.SaveResult()` RVA 0x680FB0 — 곡 종료·결과 진입, 판정 카운트 로그
- 필드: PUMPPlayer `_stat int[,] @0xD8`, `_scoreJudge float @0x164`, `_playBase @0x180`, `_mod GameModifier @0x198`

## 5. 시도했다가 실패한 것들 (Dead Ends)
- Git Bash에서 `cmd //c "ilspycmd.CMD ... \"경로 공백\""` → 경로가 공백에서 잘려 EXIT 64. **PowerShell `& "…ilspycmd.CMD" 인자`로 실행할 것**
- pe_slice `--method "PUMPPlayer::JudgeNote"` → "메서드를 찾지 못함". **네임스페이스 포함** `"PIUMobileStepDLL.PUMPPlayer::JudgeNote"` 필요
- REA `open_binary` provider 자동 선택 실패(ghidra/ida 모호) → `provider_id: "ghidra"` 지정
- GameAssembly.dll(76.8MB) 원본을 REA로 열지 말 것(타임아웃) — pe_slice 조각만
- `AddressableManager.LoadAsset<Sprite>` 제네릭 수동 패치 → IL2CPP 공유 제네릭(`LoadAsset<object>`)이 함께 걸려 부팅 시 `GameData` 로드가 깨짐. **제네릭 패치 대신 `MainStation.GAMESTART` Prefix에서 템플릿 곡으로 통과 후 Postfix에서 `PlayerData.Song`·`SelectedLevel`·`Curtain` 복원**할 것
- `NetworkManager.SendData`만 후킹하면 `GetGameStartTime` 등이 `private NetworkManager.Send`를 직접 호출해 오프라인 우회 실패 → **`NetworkManager.Send`를 직접 후킹**할 것
- `getGameStartTime` 응답을 JSON으로 주면 `DateTime.Parse` 예외 → 순수 ISO-8601 문자열(`yyyy-MM-ddTHH:mm:ssZ`) 반환
- `StepLoader.LoadUserStep` 게임 내장 UCS 파서는 멀티블록(변속) 시 `pBlock[1..]`이 `null`이 되는 원본 버그 존재 → 자체 `CustomChartParsers`로 `Step_t` 직접 구성
- `MainStage.PauseAllow` getter 패치만으로는 부분 채보 마지막 노트 이후 ESC 일시정지 해제 불가(IL2CPP가 `InGameUI.callMenu` 내부에 `get_PauseAllow`를 인라인함) → **`InGameUI.callMenu`를 직접 패치**해 호출 중 `SoloPlayer._showNoteEnd` 임시 해제할 것
- 인게임 실시간 키 입력은 구식 `KeyInput.isButtonPress`가 아니라 Unity InputSystem 기반 **`GameInputManager.Instance`**(`keyTbl5k`/`keyTbl5kSub`/`keyTbl6k`/`keyTbl6kSub` + `GetButtonState`)에서 읽을 것

## 6. 빌드 명령 및 Mods/ 배포 경로
- **통합 커스텀·오프라인 모드 (`PIURiseCustom`, 주력)**:
```powershell
dotnet build "H:\source\repos\PUMP IT UP RISE-unity-il2cpp\mod\PIURiseCustom\PIURiseCustom.csproj" -c Release -p:Deploy=true
```
  - 산출물: `mod\PIURiseCustom\bin\Release\PIURiseCustom.dll` → 배포: `H:\steam\steamapps\common\PUMP IT UP RISE\Mods\`
  - 커스텀 곡 폴더: `H:\steam\steamapps\common\PUMP IT UP RISE\hwa\` (DEFLATE/STARGAZER 표준 `hwa/<곡>/` 또는 `hwa/<앨범>/<곡>/` 규격, `info.txt`, `*.ucs`/`*.bms`, `music.ogg`/`*.mp3`, `video.mp4`/`*.webm`, `Thumbnail.png`/`cover.png`)
  - 설정 파일: `H:\steam\steamapps\common\PUMP IT UP RISE\SaveCustomKey\config.txt` (Sixtar Gate `SaveCustomKey/config.txt` 전 항목 호환, `MainStage.Start` 시 자동 핫리로드: `AutoPlay`, `AllPerfect`, `BlockSave`, `MaxScore`, 판정바, 키뷰어, 노트 흔들림, 속도 카오스, `CustomNotes/`, 서든/히든, 페이드 인/아웃, 커스텀 마라톤, `LaneCoverDebug`)
  - 선곡 화면 `CUSTOM` 채널 탭 자동 추가, 단축키 `F10`(재스캔) / `F9`(ON/OFF 토글)
- **읽기 전용 스켈레톤 (`PIURiseSkeleton`)**:
```powershell
dotnet build "H:\source\repos\PUMP IT UP RISE-unity-il2cpp\mod\PIURiseSkeleton\PIURiseSkeleton.csproj" -c Release
```
- 실행: `steam://rungameid/2756930` (exe 직접 실행 시 `SteamAPI_RestartAppIfNecessary`로 종료됨), 로그: `<게임>\MelonLoader\Latest.log`

## 6-1. 오프라인 버전 구현 (`OfflineStubPatches.cs`)
- 정적 분석은 [docs/offline_feasibility.md](docs/offline_feasibility.md).
- 구현 훅: `SteamManager.CheckTicket`, `NetworkManager.StartJwtCoroutine`, `SendHeartBeatRoutine`, `SendAuth`, `NetworkManager.Send`(private), `MaintenanceManager.TitleCheck`/`MaintenanceCheck`/`AlramNetworkError`, `PIUUtil.CommonUtil.SetPlayLogAtServer`

## 7. 검증 상태 (2026-11-22 실기 확인)
- **확인**: 오프라인 부팅 → 타이틀 → 선곡 `CUSTOM` 탭 표시 → `hwa/` 하위폴더 자켓(`MSListBox`/`EyeCatchUI`/`IngameSongInfoUI`/`ResultDefaultGameInfo`) → 프리뷰 BGA(`video.mp4`, 음소거) & 프리뷰 BGM(`music.ogg`) → `TAB` 5K/6K 전환 → `Enter` 로딩 커튼(자켓·난이도 복원) → 인게임 BGA(`file://`, 음소거) & BGM(FMOD `MusicPlayer.Load`) 재생 → `.bms` 커스텀 차트 노트 판정·부분 채보 ESC 일시정지·실시간 키뷰어(`GameInputManager`)·판정바(중앙선 1개) → 완주 후 결과 화면(`DANCE GRADE`) 진입까지 전 구간 정상 동작 확인
- **미확인**: `.ucs` 멀티블록(변속) 실제 차트 실기 검증

※ 디컴파일 코드·덤프·추출 리소스는 개인 PC 전용이며 공유하지 않는다.
