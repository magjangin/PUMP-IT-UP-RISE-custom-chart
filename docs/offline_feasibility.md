# 오프라인 버전 가능성 검토 (정적 분석만, 2026-11-22)

> 게임 서버에는 접속하지 않았다. 서버 응답은 직접 관찰하지 못했다. **확인** = 의사코드나 파일로 본 것, **추정** = 해석.

## 결론
**가능하다. 다만 "곡 플레이 + 로컬 기록" 수준까지가 현실적이다.** 서버 인증 관문은 MelonLoader 모드로 넘길 수 있다(추정, 미실측). 반면 서버가 원본을 가진 데이터(인벤토리·장비·시즌패스·하이라이트 구매 등)는 오프라인에서 사라지거나, 응답 형식을 알아야 로컬에서 흉내 낼 수 있다.

## 근거

### 1. 부팅 시 서버 인증이 관문이다 (확인)
`SteamManager.InitManager`(RVA 0x7E1830)는 아래 순서로 진행된다.
1. `SteamAPI_RestartAppIfNecessary(2756930)` 후 `SteamClient.Init`
2. `PlayerData.StartLoad()`로 세이브를 로드
3. **`await CheckTicket()`**: 서버 명령 `checkTicketAndAuth`
4. `CheckAllDLCStatus()`

CheckTicket 응답 처리(RVA 0x7E0030):
- 응답 없음: "Unknown error occurred." 팝업
- JWT 비정상: `MaintenanceManager.AuthError()`
- 정상: JWT를 저장하고 JWT 갱신·하트비트 코루틴을 시작한 뒤 `callback()`으로 부팅을 계속한다

→ 서버가 없으면 이 지점에서 멈춘다(팝업이 진행을 막는지는 추정).

### 2. 서버 주소 (확인, stringliteral.json)
- REST: `https://riselive.piugame.com:3000/`
- 멀티 WebSocket: `ws://riselive.piugame.com:3011` (개발용 `ws://risedev.piugame.com:4000`도 남아 있음)

### 3. 서버에 의존하는 기능 (확인, `NetCommand` 42종)

| 분류 | 명령 | 오프라인 영향(추정) |
|---|---|---|
| 인증·유지 | checkTicketAndAuth, heartbeat, getGameStartTime, getMaintenanceSchedule | **부팅 관문.** 가짜 성공으로 대체 필요 |
| 플레이어 데이터 | getPlayerInfos/setPlayerInfo, getPlayerInven/addPlayerInven, getPlayerEquips/setPlayerEquip | 아바타·스킨·장비 등 소유 아이템. 서버가 원본이면 로컬 흉내 필요 |
| 기록·랭킹 | setRankResult, getRank, getMyLeaderBoard, playLog | 오프라인에선 버림(no-op) |
| 시즌·재화 | Season Pass 4종, getHighlightCurrency, buyHighlight, Highlight Own/Clear | 서버 재화 기반이라 흉내 내지 않으면 사용 불가 |
| 디비전·배틀 | Division 3종, BattlePoint/Leaders/MyBattleScore, setBossClear, getSkillUpBossSongs | 온라인 경쟁 모드. 사용 불가 |
| 퀘스트·이벤트 | DailyQuest 3종, WorldMax 4종, trialDlcPlay, trialWorldMaxPlay, tryampassConnect | 사용 불가 |
| 멀티플레이 | WebSocket(MultiplayController) | 사용 불가. v0.3.2부터 `NetworkManager.InitMultiCon` Prefix에서 접속 대상을 `127.0.0.1:1`로 바꿔 외부 접속을 막는다(`MultiplayController.ConnectAsync`가 소켓을 여는 곳이지만 `CancellationToken` struct 인자 때문에 직접 후킹하면 크래시하므로 인자 없는 상위 메서드를 가로챈다). 코드로 확인한 외부 주소는 `riselive` REST·WS뿐(stringliteral 전수 검색) |

### 4. 세이브는 로컬(Steam Cloud)에 있다 (확인)
- `H:\steam\userdata\<id>\2756930\remote\`: `.data .archive .actions .favorite .option .prefs .sTable .ulData _play.log`
- `PlayerData.LoadEncrypt/SaveEncrypt`로 저장되는 암호화 파일이다(추정). 곡 클리어 기록·플레이 로그·즐겨찾기는 서버 없이 유지될 가능성이 높다
- 로컬 옵션 파일: `%USERPROFILE%\AppData\LocalLow\ANDAMIRO\PUMP IT UP RISE\Config\<SteamID>.option`

### 5. Steam·DLC
- Steam 소유권과 DLC는 Steam 클라이언트로 확인한다(`IsDLCInstalled`). Steam이 켜져 있으면 오프라인 모드에서도 정상 동작하므로 손댈 필요가 없다
- **DLC 확인·Steam DRM 우회는 이 검토 범위 밖이다.** 현재 판매 중인 게임이라 보존 목적이어도 하지 않는다. 오프라인화는 "게임 서버 대체"로만 한정한다

## 구현 경로 (다음에 할 때)
1. **MelonLoader 모드 하나로 서버 스텁 구현.**
   - `SteamManager.CheckTicket`을 Prefix로 막고 가짜 JWT를 넣은 뒤 callback을 호출한다
   - `NetworkManager.StartJwtCoroutine`/`SendHeartBeatRoutine`은 무력화한다
   - `NetworkManager.Send(NetCommand, string, Action<string>)`을 Prefix로 가로채 명령별 로컬 응답(JSON)을 돌려준다. 저장이 필요한 명령은 로컬 파일로 처리한다
2. **필요한 정보: 명령별 응답 JSON 형식.**
   - 방법 A: 정상 온라인 상태에서 `Send`의 callback에 Postfix를 걸어 응답을 로그로 받는다. 본인 계정·본인 PC에서 읽기만 한다
   - 방법 B: 응답을 역직렬화하는 타입(`JWT`, `PlayerInfo`, `PlayerEquipInfo` 등)을 덤프에서 읽어 역설계한다
3. **흉내 낼 범위 결정.** 최소안은 인증·하트비트·점검 일정만 가짜 성공으로 두고 나머지는 빈 응답이다. 이렇게 하면 곡 선택·플레이 가능 여부부터 확인할 수 있다

## 리스크·미확인
- `MaintenanceManager.AuthError()` 이후 동작(타이틀 복귀인지 종료인지) — 미확인
- 곡 해금이 서버 인벤토리(`getPlayerInven`)에 의존하는지 — 미확인. 의존하면 응답 캡처가 필수다
- 가짜 응답 상태에서 게임이 기록을 서버로 보내려 하면 실패 처리 경로가 진행을 막을 수 있다 — 미확인
- 온라인 상태로 스텁 모드를 켜면 잘못된 데이터가 서버로 갈 위험이 있다 → 스텁 모드는 서버 송신을 전부 막는 방식으로만 만든다
