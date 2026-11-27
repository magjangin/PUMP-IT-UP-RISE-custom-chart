# Changelog

## [0.3.17] - 2026-11-27

### 성능
- **마라톤 배너 폰트 캐싱 (`FindObjectOfType` 씬 전수 검색 제거)**: 마라톤 배너 생성 시 씬의 모든 오브젝트를 검색하던 `UnityEngine.Object.FindObjectOfType<TextMeshProUGUI>()` 호출을 최초 1회만 수행하도록 `_cachedBannerFont`에 캐시하여 배너 생성 시의 프리징을 방지했습니다.
- **전체 UI 자켓/커튼 스프라이트 재대입 방지 확대 (`SetSpriteIfChanged`)**: `MSListBox`에 적용했던 동일 스프라이트 재대입 방지 패턴(`cur.Pointer != sprite.Pointer`일 때만 대입)을 `SongDataCell`, `IngameSongInfoUI`, `ResultDefaultGameInfo`, `BGAPlayerLoader`, `MultiStageCreateRoomPopup` 등 모든 UI 자켓 적용 지점에 일괄 적용하여 불필요한 네이티브 호출 및 UI 그래픽 리빌드를 최소화했습니다.
- **오버레이 매 프레임 관리 null 비교 선행**: `InGameOverlayUI.UpdateOverlay`에서 `MainStage.Instance` 및 `StageController.Instance`에 대해 관리 `is null` 체크를 먼저 수행해, 씬 전환 및 스테이지 종료 시 매 프레임 불필요한 Unity `== null` 연산자 호출(IL2CPP interop)을 제거했습니다.
- **마라톤 잔여 곡 O(1) 빠른 판정**: `HasRemainingCustomSongs`에서 `_playedIds.Count >= songs.Count` 비교를 선행하여 전체 카탈로그 순회 없이 O(1)로 조기 반환합니다.

### 안정성 및 자원 관리
- **파싱 실패 Task 캐시 즉시 제거**: `CustomChartParsers`에서 차트 파싱 중 실패한 작업(`task.IsFaulted`)이 캐시에 방치되지 않도록 즉시 제거하여 캐시 슬롯 낭비를 방지하고 다음 시도 시 재파싱이 정상 동작하도록 보장했습니다.
- **차트 파서 수치 안정성 및 행 상한 안전장치**: UCS/BMS 분할 계산 시 0으로 나누기(DivideByZero/NaN)를 방어하고, 손상된 BMS 파일의 비정상적인 마디 번호로 인한 메모리 폭주를 막기 위해 최대 200,000행 상한 안전장치를 추가했습니다.

### 검증
- Release `-warnaserror` 빌드 경고 0 / 오류 0 (스켈레톤 포함).

## [0.3.16] - 2026-11-26

### 성능
- **선곡 리스트 박스 자켓 중복 대입 및 UI 리빌드 차단**: 선곡 화면에서 스크롤할 때 `MSListBox.UpdateDisplayContent` 및 `UpdateLevels` Postfix가 호출되는데, 이미 같은 자켓 스프라이트가 적용되어 있는 셀(`cur.Pointer == sprite.Pointer`)인 경우 프로퍼티 재대입과 UI 리빌드를 건너뛰도록 최적화했습니다. 빠른 스크롤 시 발생하는 불필요한 Unity 네이티브 호출을 완전히 제거했습니다.

### 버그 수정 및 안정성
- **BMS 파서 안정 정렬(Stable Sort) 보장**: C# `List<T>.Sort`는 불안정 정렬이라, 같은 박(Beat)에 여러 노트나 홀드 시작/끝이 위치할 때 순서가 뒤집힐 수 있었습니다. `BmsNote`에 파일 출현 순서 `Order`를 기록하고, 박이 같으면 `Order`로 정렬하도록 2차 비교를 추가하여 동시치기 및 홀드 노트의 페어링 순서를 100% 보존합니다.
- **BGA VideoPlayer 핸들러 수명 주기 완전 격리**: 인게임 BGA 재생 시 붙였던 `VideoPlayer` 델리게이트가 씬 전환 또는 곡 종료 후에도 남아 있을 위험을 제거하기 위해, `CustomMediaPatches.ClearStage()`를 추가하고 씬 로드(`OnSceneWasLoaded`) 및 스테이지 종료(`SaveResult`, `OnDisable`, `OnDestroy`) 시점에 명시적으로 핸들러를 분리(`Detach`)합니다.

### 업데이트 내성
- **오프라인 모드 질의 일관성 정합**: `OfflineStubPatches` 내부 16곳의 오프라인 상태 확인을 `SaveCustomConfig.OfflineMode` 기반의 `IsOffline` 속성으로 일원화하여 부팅 전용 설정값 반영의 일관성과 안전성을 강화했습니다.

### 검증
- Release `-warnaserror` 빌드 경고 0 / 오류 0 (스켈레톤 포함).

## [0.3.15] - 2026-11-26

### 업데이트 내성
- **마라톤 Enter/Esc를 Unity InputSystem으로 읽음**: 결과창 마라톤 키를 구식 `UnityEngine.Input.GetKeyDown`으로만 읽었습니다. 게임 입력은 InputSystem(`GameInputManager`) 기반이라, 플레이어 설정이 "Input System 전용"이거나 업데이트로 그렇게 바뀌면 호출마다 예외가 나 마라톤이 통째로 멈췄습니다(`[Marathon] 매 프레임 갱신 중 예외`). 이제 `Keyboard.current`(InputSystem)를 먼저 쓰고, 안 되면 구식 Input으로, 둘 다 안 되면 경고 1회 후 키만 끄고 자동 진행 타이머(`CustomMarathonAutoNext`)는 계속 동작합니다. 모드가 `Unity.InputSystem` interop을 참조합니다.

### 버그 수정
- **BMS 헤더가 파일 뒤쪽에 있으면 목록 제목이 폴더 이름으로 나오던 문제**: v0.3.13부터 차트 파서는 헤더가 데이터 뒤에 있어도 읽지만, 부팅 스캔(목록 제목·아티스트·BPM·레벨)은 앞 32KB만 봤습니다. 앞부분에 `#TITLE`이 없고 파일이 더 크면(8MB 이하) 전체를 한 번 더 훑습니다.

### 성능
- **노트 비주얼(서든·히든·페이드·커스텀 노트) 켠 상태의 노트당 Unity 비교 축소**: 알파 설정 대상 렌더러와 커스텀 노트 스킨 경로의 Unity null 비교·스프라이트 비교(IL2CPP 호출)를 관리 비교·포인터 비교로 바꿨습니다. 노트마다 매 프레임 2~5회 → 0회.

### 검증
- Release `-warnaserror` 빌드 경고 0 / 오류 0.
- 시험용 곡 폴더 실기 부팅(2026-11-26 16:17, 사용자 설정은 백업 후 복구): `hwa/MyAlbum/cover.png` + 하위 곡 2개 → `990002 [MyAlbum] 'Header At End Test'`·`990003 [MyAlbum] 'Album Song B'`로 앨범 곡 2개 인식(v0.3.14 앨범 판정 실증, 예전 방식이면 표지만 있는 곡 1개), 35KB BMS의 끝에 있는 `#TITLE`·`#ARTIST`·`#BPM 150` 읽음 → `[Patch] 65/65` → `사전 JIT 점검 통과 — 메서드 411개`(InputSystem 참조 포함) → `v0.3.15 loaded` → 오프라인 스텁·DLC 처리 정상. 마라톤 키 입력과 노트 비주얼은 미실기.

## [0.3.14] - 2026-11-25

### 버그 수정
- **앨범 폴더에 표지 이미지가 있으면 앨범 곡이 통째로 빠지던 문제**: `hwa/<앨범>/<곡>/` 배치에서 앨범 폴더에 `cover.png`·설명 `.txt` 하나만 있어도 앨범 폴더를 곡 폴더로 판정해, 하위 곡 폴더를 읽지 않고 표지만 있는 가짜 곡이 목록에 올라갔습니다. 이제 차트·음원(.ogg/.mp3/.flac)·영상이 있으면 곡 폴더, 이미지·txt만 있으면 하위 곡 폴더가 있는지 먼저 보고 앨범으로 읽습니다. 하위 곡이 없으면 예전처럼 곡 폴더입니다(자켓만 있는 뼈대 테스트 폴더 유지). 기존 곡 폴더 판정·곡 ID는 바뀌지 않습니다.
- **오디오 로드 콜백 예외 시 커스텀 음원이 공식 곡 음원으로 덮이던 문제**: `AudioManager.LoadMusicSound` 가로채기에서 커스텀 음원을 올린 뒤 게임 콜백이 예외를 던지면 전체 catch로 떨어져 원본을 실행(템플릿 공식 곡 음원을 다시 올리고 콜백을 한 번 더 호출)했습니다. 콜백 예외를 따로 잡아 경고만 남깁니다.
- **CP949(EUC-KR) 인코딩 등록 경쟁**: 차트를 백그라운드에서 미리 파싱하는 경로(v0.3.10~)와 메인 스레드가 동시에 처음 디코딩하면, 한쪽이 인코딩 공급자 등록 전에 CP949를 찾다 실패해 Latin1로 읽을 수 있었습니다(EUC-KR 제목·키음 이름 깨짐 → 홀드 키음 인식 실패 가능). 등록을 스레드 안전한 1회 초기화로 바꿨습니다.
- **오프라인 `CheckTicket` 콜백 예외 시 DLC 목록 보강 누락**: 콜백이 예외를 던지면 `PlayerData.DLCEntrys` 빈 배열 보강을 건너뛰었습니다(멀티 방 생성 창 ESC/ENTER 먹통의 원인). 콜백을 격리해 항상 보강합니다.

### 업데이트 내성
- **예전 config.txt에 새 설정 항목 자동 추가**: 이전 버전이 만든 `SaveCustomKey/config.txt`에는 이후 생긴 항목(예: v0.3.13 `UnlockAllDlc`)이 없어 설정이 있는 줄도 알 수 없었습니다. 부팅 때 빠진 키를 파일 끝에 **기본값**과 설명 주석으로 덧붙입니다(기존 줄·주석·순서는 그대로, 덧붙이기만). UTF-16 파일은 건드리지 않고, UTF-8이 아닌 파일(CP949 등)에는 인코딩이 섞이지 않게 `키=값` 줄만 붙입니다. 로그 `[Config] config.txt에 없던 새 설정 항목 N개를 파일 끝에 기본값으로 추가: …`.
- **BGA 이벤트 콜백 격리**: 커스텀 BGA `VideoPlayer` 준비/오류 이벤트에서 게임 메서드(`OnBGAPrepared`/`OnVideoError`)를 부를 때 예외를 잡아 경고로 남깁니다.

### 성능
- **키뷰어 매 프레임 Unity null 비교 제거**: 레인·키 테이블마다 `GameInputManager`를 Unity 비교(`== null`, IL2CPP 호출)해 프레임당 최대 35회 호출하던 것을 관리 비교로 바꿨습니다(호출부에서 한 번만 Unity 비교).
- **선곡 리스트 박스 갱신**: `MSListBox.UpdateDisplayContent`/`UpdateLevels` Postfix가 공식 곡에서도 자켓 이미지 Unity 비교를 먼저 하던 것을, 관리 사전 조회로 커스텀 곡인지 먼저 거르도록 순서를 바꿨습니다.

### 검증
- Release `-warnaserror` 빌드 경고 0 / 오류 0(스켈레톤 포함).
- 독립 사본 실기 부팅(2026-11-25 11:15): `[Build] … 파일 지문 일치` → `[Config] … 새 설정 항목 1개 … 추가: UnlockAllDlc` → `[Patch] 65/65` → `사전 JIT 점검 통과 — 메서드 407개` → `v0.3.14 loaded` → `CheckTicket` → `DLCEntrys` 보강 → `CheckAllDLCStatus … DLC 없음으로 처리 (UnlockAllDlc=0)` → 파일 캐시 예열 완료. 선곡·플레이는 미실기.

## [0.3.13] - 2026-11-25

### 기능
- **DLC 해금을 설정으로 켜고 끔 (`UnlockAllDlc`, 기본 0=끔)**: v0.3.9부터 오프라인 모드에서는 공식 DLC 11개를 무조건 소유 처리했습니다. 이제 `SaveCustomKey/config.txt`의 `UnlockAllDlc=1`일 때만 그렇게 하고, 기본값(0)에서는 DLC 없음으로 처리합니다. 오프라인 모드는 Steam을 초기화하지 않으므로 어느 쪽이든 Steamworks 조회는 하지 않습니다. 부팅 때만 적용되며(실행 중 변경은 `[Config] … 다시 시작해야` 경고), `OfflineMode=0`이면 예전처럼 게임 원본 확인을 씁니다. 기존 `config.txt`에 줄이 없으면 기본값(끔)이 적용됩니다.

### 버그 수정
- **BMS 헤더가 데이터 뒤에 있으면 잘못 읽던 문제**: 파일을 한 번에 읽으며 데이터 줄을 바로 처리해, `#WAV`/`#BPM` 정의가 데이터 뒤에 오거나 섞여 있으면 토큰 자릿수(2/3자리)·확장 BPM 값·홀드 키음 이름을 모르는 채로 앞쪽 데이터를 잘랐습니다. 이제 헤더를 모두 읽은 뒤 데이터 줄을 처리합니다.
  - 오프라인 회귀 비교: 헤더가 앞에 있는 파일(실곡 `hwa2.bms` + 무작위 40개 × 옵션 3종 = 123건)은 수정 전후 결과가 완전히 같음. 같은 내용에서 헤더를 뒤로 옮긴 120건은 예전 파서가 114건을 다르게 읽었고, 새 파서는 120건 모두 원본과 같음.

## [0.3.12] - 2026-11-25

### 성능
- **자켓·로딩 썸네일 PNG 백그라운드 디코딩**: 커스텀 곡 자켓과 로딩 썸네일을 `ImageConversion.LoadImage`가 메인 스레드에서 동기로 풀어, 선곡 리스트가 처음 그려질 때와 곡 시작 때 프레임이 멈췄습니다(실곡 1024×1024 자켓 약 30ms, 1888×1056 로딩 썸네일 약 60ms — 같은 파일의 관리 디코딩 시간). 새 순수 관리 코드 `PngDecoder`가 스레드 풀에서 RGBA까지 풀어 두고, 메인 스레드는 `Texture2D.LoadRawTextureData`로 픽셀만 올립니다. 자켓은 부팅 때(패치 적용 1초 동안), 로딩 썸네일은 선곡 커서가 곡에 올라갈 때 미리 풉니다. 아직 안 쓴 결과는 96MB까지만 들고 있고, 커서가 다른 곡으로 가면 직전 곡 썸네일은 버립니다.
  - PNG가 아니거나(JPEG), 인터레이스·비표준 감마·손상 파일이면 예전 `LoadImage` 경로로 폴백합니다.
  - 디코더 검증: PIL과 픽셀 단위 비교 13종(사용자 실곡 자켓 2장, RGBA/RGB/회색/회색+알파/1비트/4비트 팔레트/8비트 팔레트+tRNS/RGB 색 키/16비트 회색) 전부 일치, 잘린·손상·무작위·인터레이스·비표준 gAMA 파일 5종은 전부 폴백.
  - 로그 `[CustomJacket] 자켓 로드 성공: … — 미리 디코딩됨, 업로드 Nms` / `메인 스레드 디코딩 Nms`로 경로와 시간을 남깁니다. MelonPreferences `BackgroundImageDecode=false`로 예전 방식 복귀.

### 문서
- Steam 클라이언트 없이 부팅되는 것은 v0.3.9에서 사용자가 실기로 확인한 사항이라, v0.3.11 문서에 "미확인"으로 적었던 부분(AGENTS.md §1·§5·§6, README)을 정정했습니다.

## [0.3.11] - 2026-11-25

> v0.3.10(로딩 성능, 아래 항목)은 따로 게시하지 않았으므로 이 릴리즈에 함께 들어 있습니다.

### 업데이트 내성
- **게임 파일 지문 검사**: 부팅 `[Build]` 로그가 `GameAssembly.dll`·`global-metadata.dat` 바이트 크기를 검증 빌드 값과 비교합니다(`파일 지문 일치/불일치`). Steam이 관리하지 않는 독립 사본은 appmanifest가 없어 buildid를 읽지 못하고 `Application.version`은 패치 업데이트에도 그대로일 수 있어, 다른 빌드의 게임 파일을 덮어써도 지금까지는 경고가 없었습니다.
- **템플릿 곡은 `songTable` 곡 우선**: 커스텀 곡을 시작할 때 거쳐 가는 공식 템플릿 곡을 `acSongTable`(아케이드 표)이 먼저 캐시되는 빌드에서도 `songTable`에서 다시 정합니다(아케이드 전용 곡이 템플릿으로 남으면 GAMESTART 검사를 통과하지 못할 수 있음). 다시 정할 때 이전 템플릿의 난이도가 남지 않게 초기화합니다.
- **UI Postfix 예외 격리 마무리**: `SongDataCell.Init`, `IngameSongInfoUI.OnShow`(커튼 이미지 부분), `BGAPlayerLoader.SetInfoData`를 `try/catch`로 감싸고, 곡 ID는 `Util.SongId`로 읽습니다. 공식 곡은 Unity 객체 비교보다 먼저 관리 사전 조회로 걸러 셀 갱신마다의 IL2CPP 호출도 줄였습니다.
- **오프라인 관문 콜백 격리**: `MaintenanceManager.TitleCheck`·`MaintenanceCheck`·`NetworkManager.SendAuth` 스텁이 게임 콜백을 대신 부를 때 콜백 예외를 잡아 경고로 남깁니다(Prefix 밖으로 새면 트램펄린이 삼켜 원본 실행 여부가 기본값으로 떨어짐).

### 버그 수정
- **미디어 없는 폴더가 곡 ID를 쓰던 문제**: 차트·음원·영상·자켓이 하나도 없는 폴더(예: `info.txt`만 있음)는 등록되지 않는데도 곡 ID를 먼저 받아 `song_ids.txt`에 쓰지 않는 번호가 쌓였습니다. 이제 등록되는 폴더만 ID를 받습니다. 기존 `song_ids.txt` 번호는 그대로입니다.
- **마라톤 다음 곡 실패 시 매 프레임 재시도**: 다음 곡을 곡 표에서 찾지 못하면 조용히 돌아가, `CustomMarathonAutoNext`가 켜져 있으면 매 프레임 다시 시도했습니다. 이제 경고를 남기고 마라톤을 멈춥니다.

### 성능
- **부팅 직후 파일 캐시 예열**: 낮은 우선순위 백그라운드 스레드가 커스텀 곡의 미리듣기 음원·자켓·차트·본곡 음원·로딩 썸네일을 한 번 읽어 OS 파일 캐시에 올립니다(최대 512MB, 데이터는 들고 있지 않음, 영상 제외). 선곡 화면에서 처음 커서를 올렸을 때 미리듣기 음원을 메인 스레드가 디스크에서 직접 읽던 지연(실기 6.6MB 72ms)을 줄이기 위한 것입니다. 로그 `[hwa] 파일 캐시 예열 완료 — N개 …`.
- **매 프레임·매 판정 경로의 Unity null 비교 축소**: 오버레이(캔버스·판정바·키뷰어·진단선 루트, 판정 틱), 마라톤 결과창 참조, 노트 비주얼의 롱노트 인자처럼 모드가 직접 만들고 비우는 참조는 관리 비교(`is null`)로 바꿨습니다.

### 문서
- `docs/hooks.md` 패치 목록을 v0.3.9 이후 65개 기준으로 고치고 Steam 초기화·DLC 소유 처리 훅 5개를 오프라인 행에 추가했습니다("DLC 확인은 우회하지 않는다" 문구 정정). README 버전·오프라인 설명, AGENTS.md의 Steam 클라이언트 관련 서술(§5·§6)을 v0.3.9 이후 동작에 맞췄습니다.

## [0.3.10] - 2026-11-24

### 성능 (BMS 파싱 · BGM · BGA 로딩)
- **선곡 미리듣기를 FMOD 스트림으로 연다**: 게임의 `AudioManager.LoadMusic`은 `MusicPlayer.Load(p, CREATESAMPLE|OPENMEMORY)`라 곡 전체를 PCM으로 미리 풀었고, 미리듣기 파일이 따로 없는 커스텀 곡은 커서를 올릴 때마다 본곡 전체를 디코딩했다. 미리듣기만 `CREATESTREAM|OPENMEMORY`로 열어 FMOD 열기가 약 305ms → 약 1ms(실기). 본게임 BGM은 판정 싱크를 위해 게임 기본값 그대로. MelonPreferences `PreviewAudioStream=false`로 예전 방식 복귀 가능.
- **차트 파싱을 두 단계로 분리**: 파일 → 행 블록(`ChartModel`, 순수 관리 코드)과 행 블록 → Il2Cpp `Step_t`(`CustomChartParsers.BuildStep`, 메인 스레드). 선곡 커서가 곡에 올라가면 관리 파싱을 백그라운드에서 미리 돌리고 (경로·크기·수정 시각·오프셋) 키로 캐시해, 본게임 로딩 때는 Il2Cpp 구성(실곡 1.1~3.8ms)만 한다. 파서 로그는 모았다가 메인 스레드에서 출력.
- **BMS 관리 파싱 정리**: 빈 토큰은 문자열을 만들지 않고, 노트 박 위치는 한 번만 계산해 정렬·분할 선택·행 배치에 재사용, 빈 행은 문자열 하나를 공유. 92KB 무작위 차트 45ms → 22ms. 예전 파서와 결과가 102건(실곡·경계 사례·무작위 26개 × 옵션 3종) 모두 동일함을 오프라인 비교로 확인.
- **노트 객체 필드 쓰기 3회 → 1회**: `Note.dw`(mAttr·mVisualEffect·mBank_Param 공용체)에 한 번에 쓴다.
- **오디오 미리 읽기·Il2Cpp 배열 캐시**: 선곡 커서가 곡에 올라가면 오디오 파일을 백그라운드에서 읽고, 게임에 넘기는 Il2Cpp byte[]까지 캐시해 같은 곡을 다시 불러도 수 MB 할당·복사를 하지 않는다(FMOD는 OPENMEMORY 데이터를 복사하므로 재사용 안전).
- **BGA**: 파일 URL 캐시, 미리듣기 영상이 이미 같은 URL로 준비돼 있으면 Prepare 생략.
- **구간별 소요 시간 로그**: `[ChartPerf]`, `[CustomBGM] … FMOD 열기(스트림/전체 디코딩) Nms`, `[PreviewBGA]/[CustomBGA] 준비 완료 (Nms)`.

## [0.3.9] - 2026-11-24

### 기능·오프라인 보존 (Steam DRM 우회 & 공식 DLC 11종 전면 해금)
- **로컬 Steam API 후킹 (`OfflineStubPatches`)**: Steam 클라이언트가 켜져 있지 않아도 게임이 꺼지지 않고 완전 독립 구동되도록 C# 모드 레벨에서 Steam 종속성을 가로챕니다.
  - `SteamManager.InitManager` Prefix: 오프라인 모드일 때 `IsUseSteam = false`를 강제 설정해, 내부의 `SteamAPI_RestartAppIfNecessary`(`Application.Quit`) 및 `Steamworks.SteamClient.Init` 실행을 원천적으로 건너뛰고 세이브 로드 및 `CheckTicket`으로 직행합니다.
  - `SteamManager.CheckUseSteam` / `GetIsUseSteam` Prefix: 항상 `false` 반환.
  - `SteamManager.CheckAllDLCStatus` Prefix: Steamworks API 호출 없이 11개 공식 DLC AppID(5344930 EZ2ON, 3932570 REMIX, 4445930 VOL.2, 4528900 PHOENIX 2023, 4744660 PHOENIX 2024, 4446060/5344920 RHYTHM PASS S1/S2, OST 4종)를 `dlcOwnedList`에 직접 주입.
  - `SteamManager.IsDLCInstalled` Prefix: 항상 `true` 반환하여 게임 내 모든 DLC 소유 처리.
- **Steamless 및 Goldberg 검증**:
  - `PUMP IT UP RISE.exe`의 PE 섹션 분석 결과 SteamStub DRM(`.bind`, `.steam`)이 없는 순수 Unity 바이너리임을 확인(Steamless 언패킹 불필요).
  - 불안정한 외부 Goldberg DLL 대신 정품 원본 `steam_api64.dll`을 복원하고 모드 레벨에서 안정적으로 100% 우회하도록 구축.
- **패치 수치**: Harmony 패치 클래스 총 65개 (오프라인/DLC 패치 5개 추가).

## [0.3.8] - 2026-11-24

### 안전·버그 수정
- **온라인 플레이 로그 보호 일관화**: `OfflineMode=0`에서 `AutoPlay` 또는 `AllPerfect`로 공식 곡을 플레이할 때도 `CommonUtil.SetPlayLogAtServer` 앞단에서 전송을 보류합니다(`[OnlineGuard]`, 기존엔 커스텀 곡만 차단).
- **텍스처 디코딩 실패 누수 방지**: `LoadOrGetJacket`·`LoadSpriteFromPng`에서 `ImageConversion.LoadImage`가 실패하면 생성한 `Texture2D`를 즉시 `Destroy`합니다.
- **스테이지 종료 시 참조 정리**: `MainStage` 종료(`SaveResult`·`OnDisable`·`OnDestroy`·씬 전환) 시 `GameplayOptionPatches.ActivePlayBase` 참조와 클리어 연출 대기 상태를 함께 정리합니다.

### 업데이트 내성
- **관문 Prefix·UI Postfix 예외 격리 확대**: `AudioManager.LoadMusicSound`, `BGAPlayer.NeedCommonBgaChecker`·`Load`, `GlobalFunc.DoesAssetExists`, `EyeCatchUI.PreparePreviewVideo`·`UpdateSong`, `MainStation.CheckSongLock`, `Mk2SongLoader.IsEmpty`, `PUMPPlayer.JudgeNote`·`DrawStep`, `PlayBase.StartSong`, `IngameSongInfoUI`·`ResultDefaultGameInfo`·`MSListBox` 패치에 `try/catch`를 보강해, 게임 업데이트로 필드가 바뀌어도 원본 실행이나 공식 UI가 깨지지 않게 했습니다.
- **미사용 Unity 모듈 참조 정리**: `PIURiseCustom.csproj`에서 실제로 쓰지 않는 `UnityEngine.IMGUIModule`·`UnityEngine.TextRenderingModule` 참조를 제거했습니다.
- `SaveCustomConfig`의 `Mathf.Clamp`/`Mathf.Max`를 관리 코드 `Math.Clamp`/`MathF.Max`로 교체했습니다.

### 성능
- **노트 비주얼 핫패스 최적화**: 판정선 `WorldToScreenPoint` 투영 결과를 프레임·판정선 Y 기준으로 캐시해 노트당 카메라 투영 호출을 2회 → 1회로 절반 축소했습니다. 알파 값이 변하지 않은 노트는 `SpriteRenderer.color` setter를 건너뛰고, `NoteSway`+`NoteSpeedChaos` 동시 활성 시 `localPosition`·`position` 중복 읽기를 제거했습니다.
- **오버레이 매 프레임 비용 절감**: 판정바 틱의 기본 색을 관리 쪽에 저장해 페이드 갱신 때 `Image.color` getter(IL2CPP 호출)를 없앴고, 키뷰어의 `GameInputManager` 키 테이블 4종은 스테이지당 1회만 조회해 매 프레임 `Il2CppStructArray` 래퍼 4개 할당을 제거했습니다.
- **마라톤 배너 텍스트 캐시**: 결과창 배너 문자열이 실제로 바뀔 때만 `TextMeshProUGUI.text`를 갱신합니다.
- **부팅 스캔 I/O 절감**: 차트 헤더 메타데이터를 읽을 때 거대한 BMS 전체 대신 앞 200줄(최대 32KB)만 읽는 `Util.ReadFirstLinesSmart`를 사용합니다. `FindBgm` 3순위 정렬의 중복 `FileInfo` 조회도 제거했습니다.

### 문서
- `README.md`에 `info.txt` 작성 예시와 `SaveCustomKey/config.txt` 주요 항목 표를 추가하고, 커스텀 곡 ID 고정(`song_ids.txt`) 설명 및 `docs/hooks.md` 버전을 최신화했습니다.

## [0.3.7] - 2026-11-24

### 제거
- 표준 BMS 롱노트 지원(LNTYPE 1의 `56 51 52 53 54` 채널, `#LNOBJ`)을 뺐습니다. 쓰지 않는 방식이라 v0.3.5에서 들어간 것을 되돌립니다. 롱노트는 예전처럼 `#WAV` 키음 이름(`홀드 시작`/`홀드 끝` 등)으로만 만듭니다. 홀드 끝이 없는 시작을 단타로 남기는 처리는 유지합니다.

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
