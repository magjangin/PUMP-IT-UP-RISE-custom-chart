# PUMP IT UP RISE — 인수인계 문서 (AGENTS.md)

> 작성 2026-11-22, 최신화 2026-11-23 (v0.3.5: 클라우드 감사 PR #9 통합 — BMS 롱노트·토큰 버그, 온라인 결과 보류, config 부팅 전용 항목, 값 형식 반환 훅 제거 / v0.3.4: 마라톤 ESC 고착·프리뷰 핸들러 누적 수정, 오프라인 패치 실패 오류 승격, 매 프레임 경로 예외 격리). 버전별 변경은 [CHANGELOG.md](CHANGELOG.md). 단계별 진행 기록은 [PROGRESS.md](PROGRESS.md), 훅 전체 목록·업데이트 점검 절차는 [docs/hooks.md](docs/hooks.md).
> **확인** = 디스크·로그·의사코드로 직접 본 것, **추정** = 해석.

## 1. 게임 버전 및 고정 여부
- Steam appid 2756930, **buildid 26399668**, 게임 버전 1.6.0 (MelonLoader 로그), GameAssembly.dll 수정일 2025-11-05 (확인)
- **고정 안 됨**: appmanifest_2756930.acf `AutoUpdateBehavior 0`(자동 업데이트). DepotDownloader 다운그레이드·.acf 패치 차단은 하지 않았다
- 모드가 부팅 때 `[Build] Steam buildid=…, 게임 버전=…`를 로그에 남기고, 검증 빌드(26399668 / 1.6.0)와 다르면 경고한다 (`SafePatcher.LogGameBuildInfo`, 상수 `VerifiedSteamBuildId`/`VerifiedGameVersion`). 새 빌드에서 실기 검증을 마치면 이 상수와 이 절을 함께 갱신할 것
- 업데이트 시 RVA(docs/hooks.md)는 전부 무효. Harmony 패치는 이름 기반이라 메서드 이름이 유지되면 동작, MelonLoader가 interop 자동 재생성

## 2. 런타임 백엔드 및 디컴파일 경로
- Unity 2022.3.62f2, **IL2CPP**, global-metadata.dat **v31**(암호화 없음, 매직 AF1BB1FA)
- 로더: MelonLoader 0.7.3 (version.dll 프록시, net6 런타임, 실제 호스트는 .NET 10 hostfxr)
- 덤프: `il2cpp_dump\` (Il2CppDumper, DummyDll/dump.cs/script.json/il2cpp.h). Assembly-CSharp만 분리: `il2cpp_dump\assembly-csharp.dump.cs`
- 시그니처: `src_dummy\` (ilspycmd, 본문 없음, RVA 주석)
- interop(모드 참조용): `<게임>\MelonLoader\Il2CppAssemblies\` (게임 첫 실행 때 MelonLoader가 생성)
- 의사코드: `slices\*.dll`(pe_slice) → REA(Ghidra) → `logs\*.rea.c`
- 위 분석 산출물 폴더는 전부 `.gitignore` 대상(개인 PC 전용)

## 3. 설계 의도와 우회 사유 (Rationale)
- 스켈레톤은 **읽기 전용 Postfix**만 건다.
- 판정 관찰 지점으로 `JudgeNote`가 아니라 `JudgeStep_PostProcess`를 택한 이유: 판정 결과(JUDGE)가 확정된 뒤 집계하는 단일 지점이라 롱노트·라인 판정까지 한곳에서 잡힌다(의사코드 확인)
- 모드 배포는 `-p:Deploy=true`일 때만 Mods\로 복사 — 기본 빌드는 게임 폴더를 건드리지 않게 하려는 것
- **패치 적용을 MelonLoader에 맡기지 않는 이유 (v0.3.1)**: 기본 `PatchAll`은 패치 클래스 하나가 실패하면(업데이트로 대상 메서드가 사라짐 등) 예외로 멈춰 뒤의 패치가 빠지거나 모드 초기화 자체가 실패한다. 그래서 `[assembly: HarmonyDontPatchAll]` + `SafePatcher.ApplyAll`이 `[HarmonyPatch]` 클래스마다 `CreateClassProcessor(t).Patch()`를 따로 try/catch로 적용한다. 실패한 패치 이름은 `[Patch] … 실패 N개: …` 로그로 남고 나머지 기능은 살아 있다
- **IL2CPP Prefix 예외 = 원본 메서드 건너뜀**: Il2CppInterop 트램펄린은 패치 본문 예외를 삼키고 기본값을 반환한다. 즉 Prefix가 NRE를 내면 원본(`CacheSongTable`, `IngameSongInfoUI.OnShow`, `PlayBase.LoadStep` 등)도 실행되지 않는다. 그래서 게임 흐름의 관문에 걸린 Prefix는 전부 try/catch로 감싸고, `Song.ID` 같은 널 가능 접근은 `Util.SongId`/`Util.CurrentSongId`를 거친다
- **멀티 WebSocket은 `NetworkManager.InitMultiCon` 앞에서 "로컬 즉시 실패"로 돌린다 (v0.3.2)**: 게임의 외부 주소는 `riselive`의 REST(:3000)·WS(:3011) 둘뿐이고(stringliteral 전수 검색), REST는 `NetworkManager.Send`로 모이지만 WS는 `InitMultiCon()`(원본: `_mc`가 null이면 `new MultiplayController()`→`Init()`→`SetHost("ws://riselive.piugame.com:3011")`, 연결 상태가 아니면 `ConnectAsync`)가 `ClientWebSocket`으로 직접 연다(멀티 메뉴 F2에서 불림; ISIL로 확인). 그래서 오프라인 모드에서는 원본 실행 **전에** `_mc`를 `ws://127.0.0.1:1`(아무도 듣지 않는 로컬 포트)로 미리 만들어 넣고 원본을 그대로 실행한다. `_mc`가 이미 있으니 호스트 설정은 건너뛰고 `ConnectAsync`는 로컬에서 즉시 거부되며, 게임이 `ConnectAsync` 안의 접속 실패 경로(catch → `PostMain`으로 OnError 알림 → `SafeCloseAsync`)를 스스로 처리한다. `_mc` 생성이 실패하면 외부 접속을 막기 위해 `Task.Delay(-1)`로 폴백한다. 방 생성(F3)·ENTER·ESC까지 실기 확인(§7 17:10). 방 생성 창은 추가로 `SetSongInit`을 가로채(`MultiStageCreateRoomPopup_SetSongInit_Patch`) 원본에는 공식 템플릿 곡 ID를 넘기고 Postfix에서 제목·자켓만 커스텀으로 덮어쓴다(커스텀 ID는 자켓 스프라이트 조회가 비는 등 안전하지 않아 다른 UI와 같은 방식 적용). v0.3.0~v0.3.1에서 멀티 메뉴를 열었다면 가짜 JWT(`offline_local_jwt`)로 실제 접속이 시도됐을 가능성이 있다(네트워크 로그는 없어 미실측)
- 스텁 응답 표에 없는 `NetCommand`는 `{}`로 응답하되 `NoteUnmodeled`가 처음 한 번만 `[OfflineStub] 응답 형식 미확인 명령` 경고를 남긴다. 그 명령 직후 화면이 깨지면 `CaptureOnlineResponses`로 실제 응답을 캡처해 `BuildStubResponse`에 추가할 것
- **노트 비주얼 `LateUpdate` 훅은 조건부로만 붙인다 (v0.3.3)**: `SpliteScript`/`LongSpliteScript.LateUpdate`는 매 프레임 × 화면의 모든 노트마다 불린다. IL2CPP Harmony 훅은 본문이 바로 `return`해도 호출마다 트램펄린·인자 마샬링 비용이 들어, 기능을 다 꺼도 노트 수만큼 비용이 났다. 그래서 두 클래스에서 `[HarmonyPatch]`를 떼고(`SafePatcher` 집계 63 → 61, v0.3.5에서 로그 전용 `GetScoreAndAccuracy` 제거로 60), `NoteVisualPatches.LateUpdateHooks.Sync`가 `MainStage.Start` Postfix(config 핫리로드 직후)마다 Sway/Chaos/Sudden/Hidden/Fade/CustomNotes/LaneCoverDebug 중 하나라도 켜져 있으면 붙이고 모두 꺼지면 뗀다. 로그 `[NoteVisual] … 훅을 붙였습니다/뗐습니다`. 대상을 못 찾거나 부착이 실패하면 경고 1회 후 그 실행에서는 다시 시도하지 않는다
- **매 노트 경로에서는 `Mathf.*`·`Time.time`·`Camera.main`·`Screen.height`를 노트마다 부르지 않는다 (v0.3.3)**: interop의 `Mathf`는 관리 코드가 아니라 IL2CPP 호출이다(sxtg2와 같은 원칙). `MathF`/`Math`와 자체 `Clamp01`을 쓰고, 프레임 값은 `Mod.FrameIndex`(OnUpdate마다 +1) 기준 프레임 캐시(`RefreshFrameCache`)로 한 번만 읽는다. Unity 객체 null 비교(`op_Inequality`)도 노트당 한 번(`hasParent`/`hasPlayBase`)
- 오프라인 `NetworkManager.Send` 차단 로그는 명령별 첫 1회만 남긴다 (v0.3.3, 씬 전환마다 같은 명령이 반복돼 로그가 묻혔음)
- **오프라인 패치 실패는 경고가 아니라 오류다 (v0.3.4)**: `SafePatcher`는 실패한 패치의 바깥 클래스(= 기능 그룹)를 세고, `OfflineStubPatches` 안의 패치가 하나라도 빠졌는데 `OfflineMode=1`이면 `[Patch] 오프라인 스텁 패치 N개 적용 실패` 오류를 남긴다. 다른 기능 실패는 그 기능만 꺼지지만, 오프라인 관문이 빠지면 그 경로는 조용히 실제 서버로 나가기 때문이다. 패치 클래스를 새로 만들 때도 **기능별 정적 클래스 안에 중첩**해야 이 그룹 집계가 맞는다(최상위 패치 클래스는 `Mod` 그룹)
- **매 프레임 경로는 `Mod.OnUpdate`에서 서브시스템별 try/catch (v0.3.4)**: 업데이트로 필드·입력 API가 바뀌어 예외가 나면 MelonLoader가 프레임마다 스택을 찍어 로그가 폭주한다. 첫 예외만 `[OverlayUI]`/`[Marathon] 매 프레임 갱신 중 예외` 경고로 남긴다. 마라톤은 구식 `UnityEngine.Input.GetKeyDown`을 쓰는데, 게임이 InputSystem 전용으로 바뀌면 여기서 걸린다(현재 빌드에서 실측 안 됨)
- **`VideoPlayer` 이벤트 핸들러는 붙이기 전에 직전 것을 뗀다 (v0.3.4)**: `add_prepareCompleted`/`add_errorReceived`로 붙인 Il2Cpp 델리게이트는 떼지 않으면 같은 플레이어에 계속 쌓인다(선곡 프리뷰는 곡을 오갈 때마다 1개씩). `CustomMediaPatches.VideoHandlers`가 마지막 델리게이트 객체를 들고 있다가 `remove_*`로 뗀다(실패해도 무시)
- **Unity 객체의 "없음" 판정은 두 가지를 구분한다**: 우리가 `null`로 비운 필드는 `is null`(관리 비교, IL2CPP 호출 없음)로 먼저 걸러 매 프레임 비용을 없애고, 게임 쪽에서 파괴됐을 수 있는 객체(`ResultUI`, `MainStage` 등)는 `== null`(Unity `op_Equality`, 파괴 감지)을 유지한다
- 마라톤 전환 플래그(`_transitionTriggered`, 결과창 `DoEscape`를 막음)는 곡 시작·결과창 표시 때 항상 푼다 (v0.3.4). v0.3.3까지는 커스텀 곡 결과창에서만 풀려, 마라톤 곡을 도중에 나가고 공식 곡을 치면 ESC가 막혔다
- `config.txt`에서 줄을 **지우면** 그 항목은 기본값으로 돌아가지 않고 마지막으로 읽은 값이 게임을 재시작할 때까지 유지된다(핫리로드는 "있는 키만 덮어쓰기"). 끄려면 줄을 지우지 말고 `0`으로 바꿀 것
- `NoteVisualPatches`의 노트 기준 위치는 `GetInstanceID()` 키에 `Parent`(parentLine 포인터)·`Split`을 같이 저장하고, 값이 달라지면 다시 잡는다(풀링 재사용 방어, v0.3.2). X/Y를 한 딕셔너리로 합쳐 프레임당 조회도 반으로 줄었다
- **`MaxScore`는 표시 직전 한 군데에서만 적용한다 (v0.3.2 재설계)**: 결과 화면이 점수를 찍는 `NumberRoller.RollCombo(StatType.Score, int value, float)` Prefix에서 `value`만 곱한다. **실기 로그(2026-11-23 16:35)로 확인한 경로**: `NumberRoller.RollCombo(Score, 1000000)`. `_stat`(원본)은 건드리지 않아 배율이 누적되지 않는다. `ArcadeResultUI.ApplyScoreCoroutine(int)`·`PlayBase.GetScoreAndAccuracy`는 같은 실기에서 **한 번도 불리지 않았다**. `ApplyScoreCoroutine`은 값을 바꾸지 않고 `[미처리 경로]` 로그만 남기고, `GetScoreAndAccuracy` 로그 훅은 v0.3.5에서 뺐다(반환형 `ValueTuple<int,float>`이 값 형식이라 ConnectAsync와 같은 박싱 크래시 위험). 스테이지당 12줄 제한. **실기 확인 (2026-11-23 16:39)**: `MaxScore=2000000`에서 결과 화면 SCORE가 2000000으로 표시, 로그 `[MaxScore] … 표시값 1000000 → 2000000`, `[미처리 경로]` 로그 없음. 인게임 HUD 점수가 따로 있는지는 미확인
- **온라인 결과 보류 (v0.3.5)**: `OfflineMode=0`이어도 `NetworkManager.Send`에서 결과/기록 전송 9종(`setRankResult`, `playLog`, `setHighlightClear`, `setBossClear`, `setWorldMaxMissionTry/Clear`, `setBattlePointInfo`, `setDivisionInfo`, `updateDailyQuestProgress`)은 현재 곡이 커스텀이거나 `AutoPlay`/`AllPerfect`가 켜져 있으면 서버로 보내지 않고 `{}`로 응답한다(`[OnlineGuard]`). 커스텀 곡은 템플릿 공식 곡으로 시작하고, `AutoPlay`/`AllPerfect`는 공식 곡에도 걸리기 때문. 결과 전송 명령이 이 9종뿐인지는 캡처로 확인한 적 없다(**추정**)
- **`OfflineMode`·`CustomEnabled`는 부팅 때만 적용 (v0.3.5)**: 곡 주입(`CacheSongTable`)과 가짜 JWT가 부팅 시점에 정해진다. 실행 중 `CustomEnabled=0`이면 목록에 남은 커스텀 곡(990001…)이 템플릿 우회 없이 `GAMESTART`로 가 시작에 실패하고, `OfflineMode` 전환은 오프라인 토큰으로 실제 서버에 접속한다. 핫리로드 때 값이 바뀌면 `[Config] … 다시 시작해야` 경고만 남긴다
- `config.txt`(SaveCustomKey)가 설정 원본이다. `ModConfig`(MelonPreferences)의 `OfflineMode`/`CustomEnabled`/`CustomChartOffsetMs`는 config.txt를 읽을 때마다 덮어써진다
- 텍스트 파일(config.txt, info.txt, 차트)은 `Util.ReadAllLinesSmart`로 읽는다: BOM → 엄격 UTF-8 → CP949 → Shift-JIS → Latin1 순. EUC-KR로 저장한 info.txt 제목이 깨지지 않게 하려는 것
- BMS 롱노트는 세 방식을 받는다 (v0.3.5): 키음 이름(`holdstart`/`홀드시작` …), LNTYPE 1 채널(`56 51 52 53 54`, 같은 채널의 두 오브젝트가 시작·끝), `#LNOBJ`(직전 단타를 머리로). 끝이 없는 시작은 단타로 남기고 경고한다(예전엔 노트가 사라졌다). 토큰 길이는 채널 `03`은 항상 2자리, `08`은 `#BPMxx` 자릿수, 나머지는 `#WAVxx` 자릿수
- BMS 변환 분할(Split)은 고정 16이 아니라 노트·변속 위치를 정확히 표현하는 최소값(4,8,12,16,24,32,48,64,96 중)을 고른다. 셋잇단 양자화 오류를 없애고, 행×레인마다 Il2Cpp `Note` 객체를 만드는 비용을 줄이기 위함(실곡 `hwa2.bms`: 4800행 → 1200행)
- 커스텀 곡 ID는 `990001`부터 경로 사전순으로 부여한다(README 구버전의 `HWA_0001` 표기는 오기). 폴더를 추가·삭제하면 뒤쪽 곡 ID가 밀린다

## 4. 핵심 훅 시그니처와 주입 파이프라인
- 주입: version.dll(MelonLoader) → Mods\PIURiseCustom.dll → `OnInitializeMelon`에서 `SafePatcher.ApplyAll(HarmonyInstance)` (v0.3.5 기준 **60개 패치 클래스**, v0.3.3~v0.3.4는 61개 + 노트 비주얼 `LateUpdate` 훅 2개는 기능이 켜졌을 때만 `MainStage.Start`에서 동적 부착. v0.3.2 63/63 실기 부팅 확인 2026-11-23 17:13)
- interop 네임스페이스: 전역 타입 `Il2Cpp.MainStage`, 네임스페이스 타입 `Il2CppPIUMobileStepDLL.PUMPPlayer` (확인)
- 커스텀 곡 파이프라인 요약 (전체 표는 docs/hooks.md §4):
  1. `Mk2SongLoader.CacheSongTable/CacheArcadeSongTable` Prefix → `PIUDataBase.songTable`에 `Song` 주입
  2. `Mk2SongLoader.CacheAllChannels/IsEmpty/GetChannelSongList`, `NewChannelListUI.InitializeListForBoxes` → `CUSTOM` 채널(enum 99)
  3. `MainStation.GAMESTART` Prefix에서 템플릿 공식 곡으로 바꿔 통과 → Postfix에서 `PlayerData.Song/SelectedLevel/Curtain` 복원
  4. `PlayBase.LoadStep` Prefix(템플릿 경유) → `Step.Load(byte[])` Postfix에서 `nNote/nBlock/pBlock`만 자체 파서 결과로 교체 후 `InitData()`
  5. `AudioManager.LoadMusicSound` → `LoadMusic(byte[])`, `BGAPlayer.Load` → `VideoPlayer.url = file://…`
- 스켈레톤 훅:
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
- 인게임 실시간 키 입력은 구식 `KeyInput.isButtonPress`가 아니라 Unity InputSystem 기반 **`GameInputManager.Instance`**(`keyTbl5k`/`keyTbl5kSub`/`keyTbl6k`/`keyTbl6kSub` + `GetButtonState`)에서 읽을 것. (v0.3.1에서 미사용 `KeyInput.Awake` 추적 훅 제거)
- **방 생성 창 ESC/ENTER 먹통을 "서버 접속"이나 "커스텀 곡" 탓으로 오진** → 진짜 원인은 오프라인에서 `PlayerData.Instance.DLCEntrys`(DLC 목록, @0x80)가 null이라 `MultiStageCreateRoomPopup.SetSongInit`이 DLC 태그를 순회하다 NullReferenceException(곡 ID와 무관: 공식 템플릿 곡으로도 동일). 그 예외로 호출한 `MultiStageRoomListUI.DoCreateRoom`이 중단되어 창의 `SetKeyAction`(키 등록)이 실행되지 않아 키가 전부 죽었다. 온라인에서는 로그인 응답(JWT의 dlc)으로 채워지므로 **오프라인 스텁은 `NetworkManager._token`뿐 아니라 `PlayerData.DLCEntrys`도 빈 배열로 채워야 한다**(`OfflineStubPatches.EnsureOfflineDlcEntries`, CheckTicket 직후). 진단은 "값을 바꾸지 않는 로그 훅"(스택의 `at …SetSongInit`와 직후 `SetKeyAction` 로그 부재)으로 했다. 도구: ISIL 덤프(`dump_game_isil` → `inspect_isil_method`)로 UI 메서드 본문을 읽을 수 있다
- **`InitMultiCon`이 끝나지 않는 Task(`Task.Delay(-1)`)를 돌려주게 막기** → 멀티 방 생성 창이 `await InitMultiCon()`에서 영원히 대기해 ESC/ENTER 입력이 먹통(2026-11-23 실기). 접속 차단은 "보류"가 아니라 "로컬에서 즉시 실패"로 해 게임의 접속 실패 처리 경로를 타게 할 것
- **`MultiplayController.ConnectAsync(string, CancellationToken)`를 직접 Harmony 후킹** → 멀티 메뉴(F2)를 여는 순간 게임이 죽는다(2026-11-23 실기: `AccessViolationException at IL2CPP.il2cpp_value_box ← (il2cpp -> managed) ConnectAsync`, WER `coreclr.dll 0xc0000005`). **struct 인자(`CancellationToken`)를 받는 메서드는 Il2CppInterop 트램펄린이 박싱하다 크래시**한다. 패치 본문(Prefix)이 실행되기 전에 죽으므로 우리 로그가 한 줄도 안 남아 "새 훅은 무관"으로 오판하기 쉽다. **값 형식(struct) 인자·`Nullable<T>` 인자를 가진 메서드는 후킹하지 말고 인자 없는 상위 메서드를 후킹할 것**(`InitMultiCon`). 부팅 `62/62`가 나와도 호출 시점에야 터진다
- **MaxScore를 `CalcNewBestScore`/`ACCalcNewBestScore` Postfix에서 `_stat[0,13]`에 곱하기** → v0.3.0~v0.3.1 내내 전혀 적용되지 않았다(2026-11-23 실기: `MaxScore=2000000`인데 1,000,000 표시, v0.3.2 `[MaxScore]` 로그 0줄). 의사코드(REA)로 확인: 두 메서드는 `PlayerData.SelectedSongId/Level`로 저장된 기록을 조회해 **신기록인지 비교**할 뿐 점수를 쓰지 않고, 저장 흐름이라 `BlockSave`가 켜져 있으면 호출조차 되지 않는다. 점수 누적은 `JudgeStep_PostProcess` → `AddCount(bank, 13, 점수×factor)`(`_stat[bank,13]`·`_stat[3,13]`·`_statLocal[13]` 합산, 음수는 0으로 보정)이고, `factor`는 `1 + (노트값/…)×GameModifier+0x58`인 **보너스 배율**이지 1,000,000 정규화가 아니다. 그래서 `factor`를 곱하는 방식도 쓰지 않았다
- **MaxScore를 `PlayBase.GetScoreAndAccuracy` Postfix에서 스케일** → v0.3.2 1차 시도, 이름과 달리 결과 화면이 이 메서드를 쓰지 않아 한 번도 호출되지 않았다(실기 `[MaxScore]` 로그에 없음). **의사코드·이름만 보고 "점수 출처"를 단정하지 말고, 먼저 로그만 남기는 훅으로 실제 호출 경로를 확인할 것**
- `ScoreRoller.InitData(string)` Prefix로 표시 문자열을 다시 스케일하던 코드도 제거했다(다른 경로와 겹치면 이중 스케일, `MaxScore<1,000,000`이면 특히 위험)
- `F9`(ON/OFF)·`F10`(재스캔) 단축키는 문서에만 있었고 **구현된 적이 없다**(v0.3.1 감사에서 확인, 문서에서 삭제). F10을 구현하려면 단순 `CustomPackManager.Scan()`으로는 안 된다: 곡 ID가 스캔 순서로 다시 매겨져 이미 주입된 `songTable` 항목과 어긋나고, 새 곡은 `Mk2SongLoader.CacheSongTable`을 다시 돌리기 전엔 목록에 안 나온다
- **Windows PowerShell 5.1 + `$ErrorActionPreference = "Stop"`에서 `git push` 실행** → git이 진행 메시지(`To https://…`)를 stderr로 쓰는 것만으로 `NativeCommandError`가 나 스크립트가 멈춘다(2026-11-23 v0.3.5 게시: 태그만 푸시되고 `gh release create` 전에 중단, 릴리즈는 수동 생성). `tools/release.ps1` 게시 단계는 `Continue`로 바꾸고 `$LASTEXITCODE`로만 판정한다. 같은 이유로 `Set-Content -Encoding UTF8`은 BOM을 붙이므로 노트는 BOM 없는 UTF-8로 쓴다
- 컴퓨터 제어로 인게임 플레이를 검증하려 할 때 다른 Claude 세션이 화면 제어를 쥐고 있으면 `request_access`가 거부된다 → 로그 기반 부팅 검증 + 오프라인 재현 스크립트로 대체했음

## 6. 빌드 명령 및 Mods/ 배포 경로
- **통합 커스텀·오프라인 모드 (`PIURiseCustom`, 주력)**:
```powershell
dotnet build "H:\source\repos\PUMP IT UP RISE-unity-il2cpp\mod\PIURiseCustom\PIURiseCustom.csproj" -c Release -p:Deploy=true
```
  - 산출물: `mod\PIURiseCustom\bin\Release\PIURiseCustom.dll` → 배포: `H:\steam\steamapps\common\PUMP IT UP RISE\Mods\`
  - interop가 없으면(게임 업데이트 직후 등) 빌드가 `CheckGameReferences` 타깃에서 원인 메시지와 함께 즉시 실패한다 → 게임을 1회 실행해 재생성 후 재빌드
  - 커스텀 곡 폴더: `H:\steam\steamapps\common\PUMP IT UP RISE\hwa\` (DEFLATE/STARGAZER 표준 `hwa/<곡>/` 또는 `hwa/<앨범>/<곡>/` 규격, `info.txt`, `*.ucs`/`*.bms`, `music.ogg`/`*.mp3`, `video.mp4`/`*.webm`, `Thumbnail.png`/`cover.png`). 폴더 변경은 게임 재시작 시 반영
  - 설정 파일: `H:\steam\steamapps\common\PUMP IT UP RISE\SaveCustomKey\config.txt` (Sixtar Gate `SaveCustomKey/config.txt` 전 항목 호환, `MainStage.Start` 시 자동 핫리로드: `AutoPlay`, `AllPerfect`, `BlockSave`, `MaxScore`, 판정바, 키뷰어, 노트 흔들림, 속도 카오스, `CustomNotes/`, 서든/히든, 페이드 인/아웃, 커스텀 마라톤, `LaneCoverDebug`)
  - 선곡 화면 `CUSTOM` 채널 탭 자동 추가
- **읽기 전용 스켈레톤 (`PIURiseSkeleton`)**:
```powershell
dotnet build "H:\source\repos\PUMP IT UP RISE-unity-il2cpp\mod\PIURiseSkeleton\PIURiseSkeleton.csproj" -c Release
```
  - PIURiseCustom과 같은 훅을 중복으로 걸므로 Mods\에 함께 두지 않는다
- **릴리즈 (v0.3.5부터, `tools/release.ps1`, 게임 설치 PC에서 실행)**: `Mod.cs`의 `Version`과 `CHANGELOG.md` 최상단 `## [버전]`을 같이 올려 main에 머지한 뒤 `.\tools\release.ps1`(버전 일치 확인 → Release 빌드 `-warnaserror` → `dist\PIURiseCustom-v<버전>.zip` + SHA256 + 노트 초안) → `.\tools\release.ps1 -Publish`(main·깨끗한 트리·origin/main 일치 확인 → 태그 푸시 → `gh release create`). 같은 태그가 있으면 중단. zip에는 DLL·CHANGELOG.md·README.md를 넣는다(v0.3.3~ 릴리즈와 같은 구성). `-Publish`는 `local` 원격이 있으면 main·태그도 함께 푸시한다. 빌드에 interop DLL이 필요해 클라우드·CI에서는 못 한다
- 실행: `steam://rungameid/2756930` (exe 직접 실행 시 `SteamAPI_RestartAppIfNecessary`로 종료됨), 로그: `<게임>\MelonLoader\Latest.log`
- 부팅 정상 판정 로그: `[Build] …`, `[Patch] Harmony 패치 60/60개 적용 완료`(v0.3.5, v0.3.3~v0.3.4는 61), `PIURiseCustom v0.3.5 loaded`, `[OfflineStub] SteamManager.CheckTicket 가로챔`. 비주얼 기능을 켠 상태로 곡에 들어가면 `[NoteVisual] … LateUpdate 훅을 붙였습니다`

## 6-1. 오프라인 버전 구현 (`OfflineStubPatches.cs`)
- 정적 분석은 [docs/offline_feasibility.md](docs/offline_feasibility.md).
- 구현 훅(10개): `SteamManager.CheckTicket`, `NetworkManager.StartJwtCoroutine`, `SendHeartBeatRoutine`, `SendAuth`, `NetworkManager.Send`(private), `MaintenanceManager.TitleCheck`/`MaintenanceCheck`/`AlramNetworkError`, `PIUUtil.CommonUtil.SetPlayLogAtServer`, `NetworkManager.InitMultiCon`(private, v0.3.2, 멀티 접속 대상을 로컬로 교체)
- 11/22~23 실기 로그 전체에서 서버 접속·타임아웃·예외는 0건. 단, 스텁 응답은 "게임이 받아들이는 최소값"이며 서버 원본 응답은 캡처한 적이 없다(인벤·장비·시즌패스 화면은 빈 데이터)
- 응답 캡처(`OfflineMode=0` + MelonPreferences `CaptureOnlineResponses=true`): `<게임>\MelonLoader\net_capture.jsonl`. v0.3.1부터 표준 JSON 이스케이프(이전 버전 캡처는 역슬래시가 이스케이프되지 않아 JSON 파서가 실패할 수 있음)

## 7. 검증 상태
- **확인 (2026-11-22 실기, v0.3.0)**: 오프라인 부팅 → 타이틀 → 선곡 `CUSTOM` 탭 표시 → `hwa/` 하위폴더 자켓(`MSListBox`/`EyeCatchUI`/`IngameSongInfoUI`/`ResultDefaultGameInfo`) → 프리뷰 BGA(`video.mp4`, 음소거) & 프리뷰 BGM(`music.ogg`) → `TAB` 5K/6K 전환 → `Enter` 로딩 커튼(자켓·난이도 복원) → 인게임 BGA(`file://`, 음소거) & BGM(FMOD `MusicPlayer.Load`) 재생 → `.bms` 커스텀 차트 노트 판정·부분 채보 ESC 일시정지·실시간 키뷰어(`GameInputManager`)·판정바(중앙선 1개) → 완주 후 결과 화면(`DANCE GRADE`) 진입까지 전 구간 정상 동작 확인
- **확인 (2026-11-23 실기, v0.3.1)**: 부팅 → `[Build] 26399668 / 1.6.0` 일치 → `[Patch] 61/61` → hwa 스캔 1곡 → 오프라인 CheckTicket·Send 스텁으로 타이틀 진입까지. 경고 0 / 오류 0 빌드
- **확인 (오프라인 재현)**: 새 BMS 분할 선택을 실곡 `hwa2.bms`(오브젝트 358개)에 재현 → Split=4, 위치 오차 0
- **미확인 (v0.3.1 변경분)**: 선곡~플레이~결과 구간 재실기 — 특히 BMS Split=4 차트 실제 판정·스크롤, 오디오 바이트 캐시(프리뷰→본게임), 키뷰어 상태 변화 시에만 색 갱신. 다음 세션에서 커스텀 곡 1판으로 확인할 것
- **미확인 (이전부터)**: `.ucs` 멀티블록(변속) 실제 차트 실기 검증
- **확인 (2026-11-23 실기, v0.3.2)**: 부팅 `Patches=62~79 (실패 0)`(빌드별), 오프라인 타이틀 → 선곡 → `.bms` 1판(Split=4, 1216행) → 결과 화면까지 정상, `MaxScore=2000000` 결과 화면 SCORE 2000000 표시. 빌드 경고 0 / 오류 0
- **Perfect 수 744(v0.3.0, Split=16) → 417(v0.3.2, Split=4)는 노트 누락이 아니다 (2026-11-23 오프라인 검증)**: `hwa2.bms` 오브젝트 358개 = 탭 328 + 홀드 15쌍(시작/끝 30). 전부 Split=4 격자에 정확히 맞고(격자 밖 0) 같은 (레인,행) 중복도 0이라 탭 328 + 홀드 헤드 15 = 343개는 두 버전 모두 보존된다. 차이는 **홀드 몸통 판정**에서 온다(417−343=74 vs 744−343=401). 홀드는 총 39.25박이고 행 수가 Split에 비례해(157행 vs 628행) 홀드 틱 판정도 Split에 따라 달라지지만, 틱 수가 행 수에 정확히 비례하지는 않는다(74/157 vs 401/628)→ 게임의 홀드 틱 공식은 **미확인**. 홀드가 있는 차트는 Split 선택(`ChooseBeatSplit`)에 따라 최대 콤보·판정 총수가 달라진다는 점만 알고 있을 것
- **확인 (2026-11-23 17:10 실기, v0.3.2 멀티 메뉴)**: F2 멀티 메뉴 → F3 방 생성 창 → ESC(`DoCancel`)로 닫힘, 다시 F3 → ENTER(`DoConfirm` → `NetworkManager.CreateRoom(990001, 5)`) 크래시 없음 → 이후 ESC 정상. 접속은 `127.0.0.1:1`로 즉시 실패해 외부 통신 없음. 방은 실제로 만들어지지 않는다(서버 없음). 곡 제목·자켓이 창에 커스텀으로 뜨는지는 스크린샷으로 확인하지 못했다
- **확인 (2026-11-23 17:13 실기, v0.3.2 최종 63개 빌드)**: 부팅 `Patches=63 (실패 0)`, `PlayerData.DLCEntrys` 보강 로그, 멀티 메뉴 진입(`InitMultiCon` 로컬 대체) 후 **방 입장·빠른입장도 크래시 없이 의도대로 막힘**(사용자 확인; 방은 만들어지거나 입장되지 않는다), 새 크래시 이벤트 없음
- **미확인 (v0.3.2 변경분)**: ① `[OfflineStub] 응답 형식 미확인 명령` 경고가 실제로 어떤 명령에서 뜨는지(실기 로그에는 한 번도 없음), ② 노트 풀링 방어(Sway/Chaos 켠 상태), ③ 인게임 HUD 점수 표시 여부, ④ 방 생성 창에 커스텀 곡 제목·자켓이 제대로 뜨는지(스크린샷 미확인), ⑤ 방 목록 새로고침 등 멀티 메뉴의 나머지 조작
- **미확인 (v0.3.4 변경분, 빌드 경고 0/오류 0만 확인)**: ① 판정바 틱 페이드·알약/다이아몬드 모양(`JudgmentBarShape=1/2`)이 v0.3.3과 같게 보이는지, ② 선곡 화면에서 커스텀 곡 프리뷰 영상을 여러 번 오간 뒤에도 영상이 정상 재생되는지(`remove_prepareCompleted` 동작), ③ 마라톤 곡 도중 이탈 → 공식 곡 결과창 ESC 정상
- **미확인 (v0.3.3 변경분)**: 노트 비주얼 기능을 켠 곡/끈 곡을 번갈아 들어갈 때 훅 부착·해제가 실기에서 정상인지(`[NoteVisual]` 로그), 켠 상태의 흔들림·서든/히든 동작이 v0.3.2와 같은지. 빌드 경고 0/오류 0은 릴리즈 빌드로 확인
- **미실기 (v0.3.5, 클라우드 감사 PR #9 통합분 — 게임 PC에서 빌드 경고 0/오류 0 확인)**: 부팅 `[Patch] 60/60` → 커스텀 곡 1판(선곡→플레이→결과)을 확인할 것. 특히 볼 것: ① `CustomMediaPatches.VideoHandlers`의 `remove_prepareCompleted`/`remove_errorReceived`가 실제로 떼어지는지(컴파일은 통과) ② LNTYPE 1/`#LNOBJ` 차트 롱노트 ③ 3자리 `#WAV` 차트의 BPM ④ 마라톤 중 일시정지→나가기 후 공식 곡 결과창 Esc ⑤ `OfflineMode=0`에서 `[OnlineGuard]` 로그
- **알려진 한계 (v0.3.4~v0.3.5 감사, 미수정)**: ① 커스텀 곡 자켓 PNG를 메인 스레드에서 동기 디코딩(`ImageConversion.LoadImage`) — 큰 이미지는 처음 볼 때 끊길 수 있음(이후 캐시) ② 프리뷰에 별도 파일이 없으면 본곡 전체를 메인 스레드에서 읽음(최근 2곡·64MB 이하 캐시) ③ 곡 폴더의 차트는 첫 파일 하나만 쓰므로 난이도별 차트를 나눌 수 없고, 더블(10키) BMS는 지원하지 않으며 `GAMESTART`가 템플릿을 Single로 통과시킨다(더블 선택 시 동작 미확인) ④ `AllPerfect`·클리어 연출 유예·일시정지 허용은 공식 곡에도 적용된다
- **잠재 위험 (코드 감사, v0.3.2에서 방어 추가했으나 실측 전)**:
  - 노트 풀링: `Parent`/`Split` 검증으로 재사용을 감지해 기준 위치를 다시 잡는다. 단 게임이 재사용 시 `localPosition.x`를 초기화하지 않으면 재캡처 값에 이전 흔들림이 섞일 수 있다
  - `MaxScore`: 결과 화면 외의 점수 표시(인게임 HUD 등)가 있으면 여전히 1,000,000 기준으로 보일 수 있다. 로그의 `[미처리 경로]` 줄이나 화면을 보고 해당 경로에 훅을 추가할 것

※ 디컴파일 코드·덤프·추출 리소스는 개인 PC 전용이며 공유하지 않는다.
