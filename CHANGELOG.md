# Changelog

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
