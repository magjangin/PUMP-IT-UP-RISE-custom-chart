# 후킹 후보 — PUMP IT UP RISE (Steam buildid 26399668, GameAssembly.dll 2025-11-05)

> RVA는 이 빌드 기준이다. 게임이 업데이트되면 전부 바뀐다(Harmony 패치는 이름으로 걸리므로 영향 없음, RVA를 직접 쓰는 네이티브 훅만 영향).
> **확인** = REA 의사코드로 본문을 읽음(logs\*.rea.c). **시그니처만** = DummyDll 선언만 봄, 본문 미확인.

## 1. 플레이 판정·점수 (`PIUMobileStepDLL.PUMPPlayer : Step`)

| 메서드 | RVA | 상태 | 역할 |
|---|---|---|---|
| `bool JudgeNote(int bank, int index, ref Note note, int grade)` (override, slot 4) | 0x799AD0 | 확인 | 노트 1개 판정 진입. `_mod(+0x198)+0x46`이 false면 판정 안 함. 노트에 판정 마크(+0x14 \|= 2) 후 `JudgeUnit` 호출 |
| `bool JudgeUnit(int bank, int grade, bool bVisible, bool bNoMiss, int noteCount, int longNoteCount, int altSkinCount, bool playSound)` | 0x79A210 | 시그니처만 | JudgeNote/JudgeLine 공통 처리 |
| `void JudgeStep_PostProcess(int bank, JUDGE judgement, int noteCount, bool bJudgeLong, float factor)` | 0x79A7B0 | 확인 | **판정 1회마다 집계.** `_stat[bank,judgement]++`, 콤보(idx 0x12)/미스콤보(0x14) 증감, 점수 누적(idx 0xD), `_scoreJudge(+0x164)` 갱신, 전 노트 판정 시 `_playBase+0xE0=true`. **판정 이벤트 관찰용 1순위 Postfix 지점** |
| `int GetScore(int judgment, int combo, int noteNum, int isLongNote)` | 0x79B1F0 | 확인 | 판정별 점수. 점수표 int[6] = {1000, 1000, 500, 100, -200, -500} (global-metadata.dat 오프셋 0x97E3F0에서 확인, 인덱스 5는 `4 < judgment`면 0 반환이라 미사용; 판정 정확도 float[5] = {1, 0.7, 0.3, 0.1, 0} @0x97E570), Perfect/Great + combo>50 → +1000, 동시 3노트 ×1.5, 4노트 이상 ×2, 롱노트 Miss −300 |
| `bool JudgeLine(int Bank, Note[] pLine, int Grade, Position processingPos)` (override, slot 7) | 0x799C80 | 시그니처만 | 한 줄(동시 노트) 판정 |
| `void SetJudgeTiming(float delay = 2.5f)` | 0x7992B0 | 시그니처만 | 판정 타이밍 창 설정 |
| `void CalcScore_PostPlaying(int enchantLv)` | 0x79C170 | 시그니처만 | 플레이 종료 후 점수 계산 |
| `bool CalcNewBestScore()` / `ACCalcNewBestScore()` | 0x79BD00 / 0x79B770 | 시그니처만 | 최고 기록 갱신 판정 |
| `void SetDefaultHighSpeed(float speed)` / `SpeedProc()` | 0x797960 / 0x798B10 | 시그니처만 | 배속 |

JUDGE 열거형: 0 Perfect, 1 Great, 2 Good, 3 Bad, 4 Miss (src_dummy\PIUMobileStepDLL\JUDGE.cs).
필드: `_stat int[,] @0xD8`, `_scoreJudge float @0x164`, `_playBase PlayBase @0x180`, `_mod GameModifier @0x198`.

## 2. 스테이지 흐름 (`MainStage`, `PlayBase`, `StateManager`)

| 메서드 | RVA | 상태 | 역할 |
|---|---|---|---|
| `MainStage.Start()` | 0x67F5C0 | 시그니처만 | 인게임 씬 시작 |
| `MainStage.Update()` | 0x67FEE0 | 시그니처만 | 매 프레임 |
| `MainStage.CheckGameOver()` | 0x680D60 | 시그니처만 | 게임오버 판정 |
| `MainStage.SaveResult()` | 0x680FB0 | 확인 | this+0x50=true, 현재 State가 `State_101_DivisionChallenge`가 아니면 `CoMakeResult` 코루틴 시작 → **곡 종료/결과 진입점** |
| `PlayBase.StartSong()` | 0x6915A0 | 시그니처만 | 곡 재생 시작 |
| `PlayBase.NoteEff(int idx, int grade)` | 0x692FD0 | 시그니처만 | 판정 이펙트 표시 |
| `StateManager.SetState(State newState)` | 0x7D8DA0 | 시그니처만 | 화면(State_XXX) 전환 — 씬 흐름 추적용 |

## 3. 타이틀·플랫폼

| 메서드 | RVA | 상태 | 역할 |
|---|---|---|---|
| `TitleMain.OnShow(params object[] values)` (override) | 0x641810 | 시그니처만 | 타이틀 화면 진입 |
| `SteamManager.Awake()` / `InitManager(Action)` (async) | 0x7DA400 / 0x7DA690 | 시그니처만 | Steam 초기화 |
| `SteamManager.CheckTicket(Action)` (async Task) | 0x7DA8B0 | 시그니처만 | 인증 티켓 확인(온라인 로그인 추정) |
| `SteamManager.CheckAllDLCStatus()` | 0x7DB090 | 시그니처만 | DLC 소유 확인 |
| `SteamManager.IsDLCInstalled(uint appID)` | 0x7DBA90 | 시그니처만 | DLC 설치 확인 |

## 4. PIURiseCustom v0.3.5 실제 패치 목록 (속성 기반 60개 + 동적 2개)

v0.3.2는 63개(2026-11-23 실기 부팅 63/63), v0.3.3은 61개, v0.3.4도 61개, v0.3.5는 로그 전용 `PlayBase.GetScoreAndAccuracy` 훅(값 형식 반환 → 박싱 크래시 위험, 호출 0회)을 빼서 60개. v0.3.3부터 노트 비주얼 `LateUpdate` 훅 2개는 `SafePatcher` 집계에서 빠지고, 비주얼 기능이 하나라도 켜졌을 때만 `MainStage.Start`에서 붙는다(`NoteVisualPatches.LateUpdateHooks.Sync`).

RVA 대신 **이름**으로 걸린다. 게임 업데이트 후 `[Patch] … 실패` 로그에 나온 클래스 이름을 이 표에서 찾아 영향 기능을 판단한다.
"관문"은 Prefix가 원본 실행 여부를 좌우하는 지점 — 여기서 실패·예외가 나면 해당 화면 자체가 깨질 수 있다.

| 기능 | 대상 메서드 | 종류 | 실패 시 영향 |
|---|---|---|---|
| 곡 주입 | `Mk2SongLoader.CacheSongTable` / `CacheArcadeSongTable` | Prefix(관문, try로 격리)·Postfix | 커스텀 곡이 목록에 안 나옴. 공식 곡은 정상 |
| CUSTOM 채널 | `Mk2SongLoader.CacheAllChannels`·`IsEmpty`·`GetChannelSongList`, `NewChannelListUI.InitializeListForBoxes`·`StartSelectedChannelAni`, `NewChannelListBox.UpdateChannelSprite` | Prefix/Postfix | CUSTOM 탭 없음 또는 라벨 깨짐. `ALL TUNES`에서 커스텀 곡 선택은 가능 |
| 잠금 해제 | `MainStation.CheckSongLock` | Prefix | 커스텀 곡이 잠김 표시 |
| 곡 시작 | `MainStation.GAMESTART` | Prefix+Postfix | **커스텀 곡 시작 불가**(Addressables 검사 실패) |
| 차트 | `PlayBase.LoadStep`(관문, try), `Step.Load(byte[])` | Prefix / Postfix | 템플릿 공식 곡 차트가 그대로 나옴 |
| BGM | `AudioManager.LoadMusicSound` | Prefix | 템플릿 곡 음악이 나옴 |
| BGA | `BGAPlayer.NeedCommonBgaChecker`·`Load`, `GlobalFunc.DoesAssetExists`, `EyeCatchUI.PreparePreviewVideo` | Prefix | 공통/템플릿 BGA, 프리뷰 영상 없음 |
| 자켓·커튼 | `SongDataCell.Init`, `MSListBox.UpdateDisplayContent`·`UpdateLevels`, `EyeCatchUI.UpdateSong`, `IngameSongInfoUI.OnShow`·`SetNumberImages`·`SetLevelType`·`SetInfoData`, `BGAPlayerLoader.SetInfoData`, `ResultDefaultGameInfo.SetData`, `SongListContent.GetEyecatchSprite`·`GetArcadeEyecatchSprite` | Prefix/Postfix | 기본 이미지·템플릿 난이도 표시 |
| 멀티 방 생성 창 | `MultiStageCreateRoomPopup.SetSongInit` | Prefix(`ref songId`→공식 템플릿 곡, `__state`로 원래 ID 기억)+Postfix(제목·자켓 복원) | 방 생성 창 제목·자켓이 템플릿 곡으로 보임(오프라인 `DLCEntrys` 보강은 별도: `OfflineStubPatches.EnsureOfflineDlcEntries`) |
| 오프라인 | `SteamManager.CheckTicket`, `NetworkManager.StartJwtCoroutine`·`SendHeartBeatRoutine`·`SendAuth`·`Send`, `MaintenanceManager.TitleCheck`·`MaintenanceCheck`·`AlramNetworkError`, `CommonUtil.SetPlayLogAtServer`, `NetworkManager.InitMultiCon`(v0.3.2, 멀티 접속 대상을 `127.0.0.1:1`로 교체 — `ConnectAsync` 직접 후킹은 크래시, `Task.Delay(-1)` 보류는 UI 먹통) | Prefix(관문) | **부팅 불가 또는 서버 접속 시도** — 업데이트 후 이 중 하나라도 실패하면 `OfflineMode=0`으로 두지 말고 먼저 고칠 것 |
| 플레이 옵션 | `PUMPPlayer.JudgeNote`·`JudgeLine`·`DrawStep`·`JudgeStep_PostProcess`, `PlayBase.StartSong`, `PlayerData.SaveUserPlayResult`, `NumberRoller.RollCombo(StatType, int, float)`(MaxScore 적용 지점), `ArcadeResultUI.ApplyScoreCoroutine`(값 변경 없는 `[미처리 경로]` 로그 전용) | Prefix/Postfix | AutoPlay/AllPerfect/BlockSave/MaxScore/판정바 해당 기능만 꺼짐 (v0.3.1까지의 `CalcNewBestScore`·`ACCalcNewBestScore`·`ScoreRoller.InitData`, v0.3.4까지의 `PlayBase.GetScoreAndAccuracy`(값 형식 반환) 훅은 제거됨) |
| 클리어 연출 유예 | `PlayBase.<CoShowNoMiss>d__N.MoveNext` (`TargetMethod`로 이름 검색) | Prefix | PERFECT GAME 연출이 곡 중간에 뜸 |
| 일시정지 | `MainStage.PauseAllow`(getter), `InGameUI.callMenu` | Postfix / Prefix+Postfix | 부분 채보 후 ESC 불가 |
| 노트 비주얼 | `SpliteScript.Init`(속성), `SpliteScript.LateUpdate`·`LongSpliteScript.LateUpdate`(동적, `AccessTools.DeclaredMethod`) | Postfix | Sway/Chaos/Sudden/Hidden/Fade/CustomNotes 꺼짐. 동적 훅 실패는 `[Patch]`가 아니라 `[NoteVisual] … 실패` 경고로 나온다 |
| 마라톤 | `ResultUI.DoEscape`·`OnShow`·`OnHide` | Prefix/Postfix | 마라톤 꺼짐 |
| 스테이지 수명 | `MainStage.Start`·`SaveResult`·`OnDisable`·`OnDestroy`, `PUMPPlayer.JudgeStep_PostProcess`(판정 집계) | Prefix/Postfix | config 핫리로드·오버레이 정리 안 됨 |
| 온라인 결과 보류 | `NetworkManager.Send`(위 오프라인 행과 같은 훅, `OfflineMode=0`일 때) | Prefix | 커스텀 곡·AutoPlay·AllPerfect 결과가 서버로 전송될 수 있음 — 실패 시 온라인 플레이 금지 |

## 5. 게임 업데이트 후 점검 절차

1. `appmanifest_2756930.acf`의 `buildid` 확인 → 26399668이 아니면 업데이트된 것
2. 게임을 MelonLoader로 1회 실행 → `MelonLoader\Il2CppAssemblies\` 재생성 (모드는 일단 그대로 둬도 된다: 실패 패치만 꺼지고 부팅은 계속됨)
3. `Latest.log`에서 확인:
   - `[Build] …` 경고 줄 (검증 빌드와 다름)
   - `[Patch] Harmony 패치 N/60개 적용, 실패 M개 [그룹 K개, …]` — 실패 클래스 이름과 기능 그룹(바깥 클래스 이름)별 개수
   - `[Patch] 오프라인 스텁 패치 N개 적용 실패` **오류**(v0.3.4) — 이 줄이 있으면 6번을 먼저 해결할 것
   - `[OverlayUI]`/`[Marathon] 매 프레임 갱신 중 예외` 경고(v0.3.4) — 오버레이·마라톤이 쓰는 필드나 입력 API가 바뀐 것
   - 비주얼 기능을 켠 곡에서 `[NoteVisual] … 찾지 못해` / `… 실패` 경고 (동적 훅)
4. 실패 클래스를 §4 표에서 찾아 대상 메서드를 새 덤프(`il2cpp_dump`, ModdingSupportTool `search_reference`)에서 다시 찾는다. 이름이 바뀌었으면 패치 속성만 고치면 된다
5. `dotnet build … -c Release` — interop 시그니처가 바뀐 메서드는 컴파일 오류로 드러난다 (interop 미생성이면 `CheckGameReferences`가 원인 메시지를 낸다)
6. 오프라인 훅 10종(§4 '오프라인' 행, 멀티 WebSocket 차단 포함)이 전부 적용될 때까지는 `OfflineMode=1` 유지 + 온라인 접속 금지
7. 커스텀 곡 1판(선곡 → 플레이 → 결과) 실기 확인 후 `SafePatcher.VerifiedSteamBuildId`/`VerifiedGameVersion`, AGENTS.md §1·§7, 이 문서 제목의 빌드 번호를 갱신
8. RVA가 필요한 분석(§1~3 표)은 새 GameAssembly.dll 기준으로 다시 뽑아야 한다

## 주의 (추정 포함)

- `NetworkManager`, `MultiStage`, `GameBeginPacket/GameEndPacket`, `PlayerNetInfo` 등이 있어 **온라인 기록·멀티 기능이 있다.** 점수/판정을 바꾸는 패치는 온라인 기록 오염·계정 제재 위험이 있으므로 스켈레톤은 **읽기 전용 Postfix(로그)** 만 넣었다.
- DLC 확인(`IsDLCInstalled`) 우회는 하지 않는다. 이 문서에는 위치만 기록한다.
