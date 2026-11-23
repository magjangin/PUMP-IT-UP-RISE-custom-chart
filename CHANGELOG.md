# Changelog

## [0.3.6] - 2026-11-24

### 업데이트 내성
- **부팅 시 사전 JIT 점검**: 패치를 걸기 전에 패치 본문과 도우미 메서드를 미리 컴파일해, 게임 업데이트로 사라진 필드·메서드 참조를 부팅 로그에서 바로 드러냅니다. 깨진 패치는 아예 걸지 않아 원본이 그대로 동작합니다(예전에는 처음 불릴 때 예외가 삼켜져, Prefix라면 원본까지 조용히 건너뛰었음). 첫 판정·첫 곡 진입 때의 JIT 지연도 부팅 시간으로 옮겨집니다. 로그 `[Patch] 사전 JIT 점검 통과 — 메서드 N개`.

### 기능·수정
- **커스텀 곡 ID 고정**: `SaveCustomKey/song_ids.txt`에 폴더별 곡 ID를 기억합니다. `hwa/`에 폴더를 끼워 넣거나 지워도 다른 곡의 ID(990001…)가 밀리지 않습니다. 표가 없는 첫 실행은 예전과 같은 번호를 매깁니다.
- **config.txt 줄 삭제 = 기본값 복귀**: 핫리로드 때 기본 템플릿 값을 먼저 깔고 파일 값을 덮습니다. 이 모드가 쓰지 않는 키(오타·다른 게임 전용)는 `[Config] … 무시합니다` 한 줄로 알려줍니다.
- BMS `#LNOBJ`: 앞에 단타가 없는 끝 표식이 단타로 하나 더 생기던 문제(이제 무시하고 경고).

### 성능
- AutoPlay 키음 이름을 판정마다가 아니라 곡마다 한 번만 찾습니다(판정당 IL2CPP 사전 조회 2~3회 제거).

### 검증
- 게임 실행(Steam 26399668 / 1.6.0): `[Patch] 60/60`, 사전 JIT 점검 352개 통과, 기존 곡 ID 990001 유지, 오프라인 스텁 정상. 선곡~플레이 구간은 미실기.

## [0.3.5] - 2026-11-23

> 클라우드 감사 세션의 초안 PR #9(같은 날 v0.3.4로 작성)를 v0.3.4 위에 통합한 버전입니다. 게임 PC에서 빌드 경고 0/오류 0 확인, 실기 전.

### 수정
- BMS: 3자리 `#WAV`(`#WAV001`) 차트에서 BPM 채널 `03`도 3자리로 잘라 엉뚱한 BPM(예: `780000` → 1920)으로 읽던 문제. `03`은 항상 2자리, `08`은 `#BPMxx` 자릿수를 따릅니다.
- BMS: 끝(홀드 끝 키음)이 없는 롱노트 시작이 노트째로 사라지던 문제 → 단타로 남기고 경고합니다. 끝 없이 같은 레인에서 다시 시작하면 앞의 시작도 단타가 됩니다.
- BMS: 탭으로 구분한 헤더(`#BPM\t140`, `#WAV01\t홀드 시작.wav`, `#TITLE\t…`)를 읽지 못하던 문제.
- 마라톤: v0.3.4의 전환 플래그 해제에 더해, 곡 시작 때 지난 결과창 참조(`_activeResultUI`)도 비웁니다.
- `config.txt`를 저장하는 순간 읽기가 실패하면 수정 시각만 기록되어 다음 저장 전까지 다시 읽지 않던 문제(읽기에 성공한 뒤에만 기록).
- override 슬롯(`hwa/override/`)도 "커스텀 곡"으로 판정되던 문제(`IsCatalogSongId`가 실제 목록 주입 곡만 봄). 곡 번호 부여는 그대로라 기존 ID는 바뀌지 않습니다.
- 차트 주입 슬롯(`_pendingCustomPack`)을 한 번 쓰면 비웁니다. `LoadStep`을 거치지 않는 다음 `Step.Load`에 지난 커스텀 차트가 들어갈 여지를 없앴습니다.

### 추가
- BMS 표준 롱노트: LNTYPE 1 채널(`56 51 52 53 54`)과 `#LNOBJ`. 키음 이름 방식과 함께 쓸 수 있습니다.

### 안전
- **온라인 모드(`OfflineMode=0`)에서도** 커스텀 곡·`AutoPlay`·`AllPerfect` 플레이의 결과/기록 전송(`setRankResult`, `playLog` 등 9종)은 서버로 보내지 않습니다(`[OnlineGuard]` 로그). 커스텀 곡은 템플릿 공식 곡을 거쳐 시작하므로 결과가 공식 기록에 섞일 수 있었습니다.
- `OfflineMode`·`CustomEnabled`는 부팅 때만 적용합니다. 실행 중에 바꾸면 경고만 남깁니다(곡 주입·가짜 JWT가 부팅 때 정해져 실행 중 전환은 커스텀 곡 시작 실패나 오프라인 토큰으로 서버 접속을 일으킴).
- 로그 전용 `PlayBase.GetScoreAndAccuracy` 훅 제거: 한 번도 불리지 않았고 반환형이 값 형식(`ValueTuple`)이라 `ConnectAsync` 크래시와 같은 박싱 위험이 있었습니다. 패치 수 61 → **60**.
- 반환값이 있는 자켓 Postfix(`SongListContent.GetEyecatchSprite`·`GetArcadeEyecatchSprite`)를 try로 격리(예외 시 공식 곡 자켓까지 null이 되던 위험). `MainStage.Start` Postfix 단계별 격리.

### 성능
- 판정마다 도는 `JudgeLine` Prefix에서 `Mathf.Abs`(IL2CPP 호출)를 `Math.Abs`로 교체.

### 릴리즈 도구
- `tools/release.ps1`: Mod.cs·CHANGELOG 버전 일치 확인 → 경고를 오류로 보는 Release 빌드 → `dist/` zip(DLL·CHANGELOG·README)·SHA256·릴리즈 노트 → `-Publish`로 태그·`gh release`.

## [0.3.4] - 2026-11-23

### 버그 수정
- 커스텀 마라톤: 결과창에서 다음 곡으로 넘어가다 그 곡을 도중에 나가고 공식 곡을 치면, 공식 곡 결과창에서 ESC가 계속 막히던 문제. 전환 플래그를 곡 시작(`MainStage.Start`)과 결과창 표시 때 항상 풉니다.
- 선곡 화면에서 커스텀 곡 사이를 오갈 때마다 프리뷰 영상(`VideoPlayer`)의 준비 완료 핸들러가 하나씩 쌓이던 문제. 인게임 BGA도 같은 방식으로 직전 핸들러를 떼고 붙입니다.

### 업데이트 내성
- 오프라인 스텁 패치가 하나라도 적용에 실패하면 `[Patch] 오프라인 스텁 패치 N개 적용 실패` **오류**를 남깁니다(그 경로는 실제 서버로 나갈 수 있음). 실패 요약에 기능 그룹별 개수(`[OfflineStubPatches 1개, …]`)도 붙습니다.
- 매 프레임 경로(오버레이·마라톤)에서 예외가 나면 첫 1회만 경고하고 나머지 기능은 계속 동작합니다(이전에는 프레임마다 스택이 찍혀 로그가 폭주할 수 있었음).
- `MainStation.GAMESTART`·`IngameSongInfoUI.OnShow` Prefix를 try/catch로 격리(예외가 새면 원본까지 건너뛰어 공식 곡도 시작되지 않음).
- 노트 비주얼 훅이 반쯤 붙다 실패하면 붙은 쪽을 되돌립니다(기능을 꺼도 떼지 못하는 상태 방지).
- `PIURiseSkeleton.csproj`에도 interop 미생성 시 원인을 알려주는 `CheckGameReferences` 추가.

### 성능
- 판정바 틱의 표시 상태를 관리 쪽에 기억해 매 프레임 `gameObject.activeSelf` 조회(틱 24개 × IL2CPP 호출 2회)를 없앴습니다.
- 판정바 알약·다이아몬드 모양 스프라이트를 픽셀마다 `SetPixel`·`Mathf` 대신 관리 배열 + `SetPixels32` 한 번으로 생성(첫 곡 시작 때 약 3만 번의 IL2CPP 호출 제거).
- 판정바·키뷰어·진단선이 모두 꺼져 있으면 오버레이 갱신이 매 프레임 `MainStage`/`StageController` 조회 없이 바로 끝납니다. 마라톤이 꺼져 있으면 배너 정리도 건너뜁니다.

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
