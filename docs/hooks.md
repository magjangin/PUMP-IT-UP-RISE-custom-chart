# 후킹 후보 — PUMP IT UP RISE (Steam buildid 26399668, GameAssembly.dll 2025-11-05)

> RVA는 이 빌드 기준이다. 게임이 업데이트되면 전부 바뀐다(Harmony 패치는 이름으로 걸리므로 영향 없음, RVA를 직접 쓰는 네이티브 훅만 영향).
> **확인** = REA 의사코드로 본문을 읽음(logs\*.rea.c). **시그니처만** = DummyDll 선언만 봄, 본문 미확인.

## 1. 플레이 판정·점수 (`PIUMobileStepDLL.PUMPPlayer : Step`)

| 메서드 | RVA | 상태 | 역할 |
|---|---|---|---|
| `bool JudgeNote(int bank, int index, ref Note note, int grade)` (override, slot 4) | 0x799AD0 | 확인 | 노트 1개 판정 진입. `_mod(+0x198)+0x46`이 false면 판정 안 함. 노트에 판정 마크(+0x14 \|= 2) 후 `JudgeUnit` 호출 |
| `bool JudgeUnit(int bank, int grade, bool bVisible, bool bNoMiss, int noteCount, int longNoteCount, int altSkinCount, bool playSound)` | 0x79A210 | 시그니처만 | JudgeNote/JudgeLine 공통 처리 |
| `void JudgeStep_PostProcess(int bank, JUDGE judgement, int noteCount, bool bJudgeLong, float factor)` | 0x79A7B0 | 확인 | **판정 1회마다 집계.** `_stat[bank,judgement]++`, 콤보(idx 0x12)/미스콤보(0x14) 증감, 점수 누적(idx 0xD), `_scoreJudge(+0x164)` 갱신, 전 노트 판정 시 `_playBase+0xE0=true`. **판정 이벤트 관찰용 1순위 Postfix 지점** |
| `int GetScore(int judgment, int combo, int noteNum, int isLongNote)` | 0x79B1F0 | 확인 | 판정별 점수. 점수표 int[6](상수, 값 미확인), Perfect/Great + combo>50 → +1000, 동시 3노트 ×1.5, 4노트 이상 ×2, 롱노트 Miss −300 |
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

## 주의 (추정 포함)

- `NetworkManager`, `MultiStage`, `GameBeginPacket/GameEndPacket`, `PlayerNetInfo` 등이 있어 **온라인 기록·멀티 기능이 있다.** 점수/판정을 바꾸는 패치는 온라인 기록 오염·계정 제재 위험이 있으므로 스켈레톤은 **읽기 전용 Postfix(로그)** 만 넣었다.
- DLC 확인(`IsDLCInstalled`) 우회는 하지 않는다. 이 문서에는 위치만 기록한다.
