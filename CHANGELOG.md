# Changelog

## [0.3.4] - 2026-11-23

> 클라우드 감사 세션에서 수정했습니다. 게임 interop DLL이 없는 환경이라 **빌드·실기 확인 전**입니다. 릴리즈 전에 게임 PC에서 `tools\release.ps1`로 빌드하고 커스텀 곡 1판을 확인하세요.

### 수정
- BMS: 3자리 `#WAV`(`#WAV001`) 차트에서 BPM 채널 `03`도 3자리로 잘라 엉뚱한 BPM(예: `780000` → 1920)으로 읽던 문제. `03`은 항상 2자리, `08`은 `#BPMxx` 자릿수를 따릅니다.
- BMS: 끝(홀드 끝 키음)이 없는 롱노트 시작이 노트째로 사라지던 문제 → 단타로 남기고 경고합니다. 끝 없이 같은 레인에서 다시 시작하면 앞의 시작도 단타가 됩니다.
- BMS: 탭으로 구분한 헤더(`#BPM\t140`, `#WAV01\t홀드 시작.wav`, `#TITLE\t…`)를 읽지 못하던 문제.
- 마라톤으로 다음 곡에 들어간 뒤 결과창을 거치지 않고 나가면, 그 뒤 모든 결과창(공식 곡 포함)에서 Esc가 막히던 문제.
- `config.txt`를 저장하는 순간 읽기가 실패하면 수정 시각만 기록되어 다음 저장 전까지 다시 읽지 않던 문제(읽기에 성공한 뒤에만 기록).
- override 슬롯(`hwa/override/`)도 "커스텀 곡"으로 판정되던 문제(`IsCatalogSongId`가 실제 목록 주입 곡만 봄). 곡 번호 부여는 그대로라 기존 ID는 바뀌지 않습니다.
- 차트 주입 슬롯(`_pendingCustomPack`)을 한 번 쓰면 비웁니다. `LoadStep`을 거치지 않는 다음 `Step.Load`에 지난 커스텀 차트가 들어갈 여지를 없앴습니다.
- 선곡 화면에서 커서를 옮길 때마다 프리뷰 `VideoPlayer`에 `prepareCompleted` 핸들러가 계속 쌓이던 문제(다음 주입 전에 앞의 핸들러를 뗌). 인게임 BGA도 같은 방식.

### 추가
- BMS 표준 롱노트: LNTYPE 1 채널(`56 51 52 53 54`)과 `#LNOBJ`. 키음 이름 방식과 함께 쓸 수 있습니다.

### 안전
- **온라인 모드(`OfflineMode=0`)에서도** 커스텀 곡·`AutoPlay`·`AllPerfect` 플레이의 결과/기록 전송(`setRankResult`, `playLog` 등 9종)은 서버로 보내지 않습니다(`[OnlineGuard]` 로그). 커스텀 곡은 템플릿 공식 곡을 거쳐 시작하므로 결과가 공식 기록에 섞일 수 있었습니다.
- `OfflineMode`·`CustomEnabled`는 부팅 때만 적용합니다. 실행 중에 바꾸면 경고만 남깁니다(곡 주입·가짜 JWT가 부팅 때 정해져 실행 중 전환은 커스텀 곡 시작 실패나 오프라인 토큰으로 서버 접속을 일으킴).
- 로그 전용 `PlayBase.GetScoreAndAccuracy` 훅 제거: 한 번도 불리지 않았고 반환형이 값 형식(`ValueTuple`)이라 `ConnectAsync` 크래시와 같은 박싱 위험이 있었습니다. 패치 수 61 → **60**.
- 반환값이 있는 자켓 Postfix(`SongListContent.GetEyecatchSprite`·`GetArcadeEyecatchSprite`)를 try로 격리(예외 시 공식 곡 자켓까지 null이 되던 위험). `MainStage.Start` Postfix 단계별 격리.

### 성능
- 판정마다 도는 `JudgeLine` Prefix와 판정바 갱신에서 `Mathf.*`(IL2CPP 호출)를 `Math`로 교체.

## [0.3.3] - 2026-11-23

### 성능
- 노트 비주얼 `LateUpdate` 훅(`SpliteScript`·`LongSpliteScript`)을 항상 걸지 않고, `NoteSway`·`NoteSpeedChaos`·`Sudden`/`Hidden`·`FadeIn`/`FadeOut`·`CustomNotes`·`LaneCoverDebug` 중 하나라도 켜졌을 때만 곡 시작(`MainStage.Start`) 때 붙입니다. 모두 꺼져 있으면 매 프레임 × 노트 수만큼 돌던 Harmony 트램펄린 비용이 사라집니다.
- 기능을 켠 경우에도 노트마다 부르던 `Mathf.*`·`Time.time`·`Camera.main`·`Screen.height`(모두 IL2CPP 호출)를 `MathF`와 프레임당 1회 캐시로 바꿨습니다.

### 로그
- 오프라인 모드의 `NetworkManager.Send` 차단 로그를 명령별 첫 1회만 남깁니다.
- 부팅 시 `[Patch] Harmony 패치 61/61개`(v0.3.2는 63개)로 보이는 것은 위 동적 훅 2개가 집계에서 빠졌기 때문입니다.

## [0.3.2] - 2026-11-23

- 멀티 WebSocket 접속 대상을 로컬(`127.0.0.1:1`)로 교체해 오프라인에서 외부 통신 0 (`NetworkManager.InitMultiCon`).
- 오프라인에서 `PlayerData.DLCEntrys`가 null이라 방 생성 창 ESC/ENTER가 먹통이던 문제 수정.
- `MaxScore`를 결과 화면 표시 경로(`NumberRoller.RollCombo(Score)`)에서 적용.
- 방 생성 창 커스텀 곡 제목·자켓 표시, 노트 풀링 재사용 방어, 응답 형식 미확인 서버 명령 경고.

## [0.3.1] - 2026-11-23

- 패치 클래스별 격리 적용(`SafePatcher`), 검증 빌드 대조 경고, 관문 Prefix 예외 격리.
- 자켓·오디오 캐시, 키뷰어 갱신 최소화, BMS 분할 자동 선택(셋잇단 정확도), 텍스트 인코딩 자동 판별.

## [0.3.0] - 2026-11-22

- 첫 실기 동작 버전: `CUSTOM` 채널, `hwa/` 커스텀 곡, 자체 UCS/BMS 파서, 커스텀 BGM·BGA·자켓, 오프라인 서버 스텁, 플레이 옵션·오버레이.
