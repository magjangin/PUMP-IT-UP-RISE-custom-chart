# Changelog

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
