# PUMP IT UP RISE Custom Chart Mod (`PIURiseCustom`)

**PUMP IT UP RISE** (Steam / Unity 2022.3 IL2CPP)용 MelonLoader 기반 **커스텀 차트 · 미디어 주입 · 오프라인 모드** 통합 프로젝트입니다.

- **현재 버전:** `v0.3.4`
- **제작자:** 화영왕 (`magjangin`)
- **대상 게임:** PUMP IT UP RISE (Steam AppID `2756930`, Build `26399668` 기준 실기 검증)
- **로더:** MelonLoader `0.7.3` (`net6.0` / IL2CPP)

---

## 주요 기능

### 1. 커스텀 곡 & `CUSTOM` 채널 자동 추가
- 선곡 화면에 **`CUSTOM` 채널 탭**을 자동으로 추가하고, 게임 폴더 내 `hwa/` 디렉터리의 커스텀 곡을 독립 곡(곡 ID `990001`~, 폴더 경로 사전순)으로 주입합니다.
- `TAB` 키로 **5K / 6K** 전환, 로딩 커튼 자켓·난이도 표시, 결과 화면(`DANCE GRADE`) 진입까지 전 구간을 지원합니다.
- `hwa/` 폴더에 곡을 추가·삭제한 내용은 **게임을 다시 시작하면** 반영됩니다. (이전 문서의 `F9`/`F10` 단축키는 구현된 적이 없어 삭제했습니다.)

### 2. 자체 차트 파서 (`.ucs` / `.bms` / `.bme` / `.bml`)
- 게임 내장 `StepLoader.LoadUserStep`의 멀티블록(변속) 버그를 우회하여 `PIUMobileStepDLL.Step_t` 구조를 직접 구성합니다.
- `.ucs`(5/10레인) 및 5키 `.bms`/`.bme`/`.bml`(채널 `16 11 12 13 14` → ↙ ↖ ◎ ↗ ↘), 롱노트(키음 이름 `holdstart`/`홀드시작` 등으로 페어링, LNTYPE 1 채널 `56 51 52 53 54`, `#LNOBJ`), 변속을 지원합니다. 끝이 없는 롱노트 시작은 단타로 배치하고 경고합니다.
- BMS는 노트 위치를 정확히 표현하는 최소 분할을 자동으로 골라 셋잇단(12·24분 등)도 어긋나지 않게 변환합니다.
- `info.txt`·차트·`config.txt`는 UTF-8 / EUC-KR(CP949) / Shift-JIS를 자동 판별해 읽습니다.

### 3. 커스텀 BGM · BGA · 자켓 · 로딩 썸네일
- **BGM:** `.ogg`, `.mp3`, `.wav`, `.flac` (FMOD `MusicPlayer.Load` 직접 로드, 프리뷰 BGM 별도 지원)
- **BGA:** `.mp4`, `.webm` (선곡 프리뷰 및 인게임 BGA 재생)
- **자켓 & 썸네일:** `cover.png`, `jacket.png`, `Thumbnail.png` 등 자동 인식 + 16:9 이미지 로딩 썸네일 지원

### 4. 오프라인 서버 스텁 (`OfflineMode`)
- 커스텀 플레이 중 온라인 랭킹/기록 오염을 방지하기 위해 기본값으로 게임 서버 통신을 차단하고 로컬 스텁 응답을 반환합니다.
- 멀티플레이 WebSocket 접속 대상을 로컬(`127.0.0.1:1`)로 바꿔(`NetworkManager.InitMultiCon`), 오프라인 모드에서는 외부로 나가는 통신이 없습니다. 멀티 메뉴는 열리지만 접속은 즉시 실패합니다.
- 온라인 모드(`OfflineMode=0`)에서도 커스텀 곡·`AutoPlay`·`AllPerfect` 플레이의 결과 전송은 서버로 보내지 않습니다(`[OnlineGuard]`).
- `OfflineMode`·`CustomEnabled` 변경은 게임을 다시 시작해야 적용됩니다(다른 설정은 다음 곡부터 핫리로드).
- 응답 형식을 모르는 서버 명령은 빈 객체 `{}`로 응답하고 로그에 한 번 경고합니다(`[OfflineStub] 응답 형식 미확인 명령`).
- Steam 소유권 및 DLC 검증(`IsDLCInstalled`)은 우회하지 않으며, 오프라인 모드에서도 정상 실행되는 Steam 클라이언트를 그대로 사용합니다.

### 5. 플레이 옵션 & 인게임 오버레이 (`SaveCustomKey/config.txt`)
Sixtar Gate: STARTRAIL / DEFLATE 통합 규격과 호환되는 설정 파일을 지원하며, **수정 후 저장하면 다음 곡 진입 시 자동 핫리로드**됩니다.
- **플레이:** `AutoPlay`, `AllPerfect`, `BlockSave`, `MaxScore`, `CustomChartOffsetMs`
- **판정바:** 가로/세로 방향, 모양(사각/알약/삼각), 위치(`Left`/`Center`/`Right`)
- **실시간 키뷰어:** `GameInputManager` 기반 5K/6K 실시간 키 입력 표시 및 색상 커스터마이징
- **비주얼 모디파이어:** 노트 흔들림(`NoteSway`), 속도 카오스(`NoteSpeedChaos`), 커스텀 노트 스킨(`CustomNotes`), 서든/히든(`Sudden`/`Hidden`), 페이드 인/아웃(`FadeIn`/`FadeOut`)
  - 모두 꺼져 있으면 매 노트 훅을 아예 걸지 않아 프레임 부담이 없습니다(곡 시작 때 설정을 보고 자동으로 붙이거나 뗌, v0.3.3).
- **커스텀 마라톤:** `CustomMarathon`, `CustomMarathonAutoNext`

---

## 설치 및 사용 방법

1. 게임 폴더에 **MelonLoader 0.7.3**을 설치하고 게임을 1회 실행해 `MelonLoader/Il2CppAssemblies/`를 생성합니다.
2. 빌드한 `PIURiseCustom.dll`을 `<게임 폴더>\Mods\`에 복사합니다.
3. `<게임 폴더>\hwa\` 아래에 커스텀 곡 폴더를 배치합니다.

### `hwa/` 폴더 구조 예시

1단계 곡 폴더와 2단계 앨범 폴더 구성을 모두 지원합니다.

```text
<PUMP IT UP RISE 설치 폴더>/
├─ Mods/
│  └─ PIURiseCustom.dll
├─ SaveCustomKey/
│  └─ config.txt          # 최초 실행 시 자동 생성
└─ hwa/
   ├─ <곡 폴더>/
   │  ├─ info.txt         # (선택) 제목, 아티스트, BPM, 레벨 정보
   │  ├─ chart.bms        # 또는 .ucs / .bme / .bml
   │  ├─ music.ogg        # 또는 .mp3 / .wav / .flac
   │  ├─ music_preview.ogg # (선택) 미리듣기 BGM
   │  ├─ video.mp4        # (선택) BGA (.mp4 / .webm)
   │  └─ cover.png        # (선택) 자켓 (cover/jacket/Thumbnail 등)
   └─ <앨범 폴더>/
      └─ <곡 폴더>/
         └─ ...
```

---

## 소스 빌드 방법

.NET 6.0 이상 SDK와 MelonLoader가 1회 실행된 게임 폴더(`Il2CppAssemblies` 생성 완료 상태)가 필요합니다.

```powershell
# 일반 빌드 (게임 폴더에 복사하지 않음)
dotnet build "mod/PIURiseCustom/PIURiseCustom.csproj" -c Release -p:GamePath="C:\Program Files (x86)\Steam\steamapps\common\PUMP IT UP RISE"

# 빌드 후 Mods/ 폴더로 자동 배포
dotnet build "mod/PIURiseCustom/PIURiseCustom.csproj" -c Release -p:GamePath="C:\Program Files (x86)\Steam\steamapps\common\PUMP IT UP RISE" -p:Deploy=true
```

---

`Il2CppAssemblies`가 없으면 빌드가 원인 메시지와 함께 바로 멈춥니다. 게임을 MelonLoader로 한 번 실행한 뒤 다시 빌드하세요.

---

## 게임 업데이트 대응

- 모드는 부팅할 때 `MelonLoader/Latest.log`에 Steam 빌드 번호와 게임 버전을 남기고, 검증된 빌드(`26399668` / `1.6.0`)와 다르면 경고합니다.
- Harmony 패치는 **기능별로 따로 적용**됩니다. 업데이트로 일부 메서드가 바뀌어도 그 기능만 꺼지고 나머지는 동작하며, 로그의 `[Patch] … 실패` 줄에 꺼진 패치 이름이 나옵니다.
- 점검 순서와 패치별 영향 표: [docs/hooks.md](docs/hooks.md) §4·§5

---

## 저장소 구조

```text
.
├─ mod/
│  ├─ PIURiseCustom/      # 통합 커스텀 차트 · 오프라인 주력 모드 (v0.3.3)
│  └─ PIURiseSkeleton/    # 읽기 전용 판정 관찰용 최소 스켈레톤 모드 (PIURiseCustom과 함께 설치하지 말 것)
├─ docs/
│  ├─ hooks.md            # 후킹 타깃, 전체 패치 목록, 업데이트 점검 절차
│  └─ offline_feasibility.md # 오프라인 스텁 설계 분석
├─ CHANGELOG.md           # 버전별 변경 사항
├─ AGENTS.md              # 에이전트/개발 인수인계 문서
├─ CLAUDE.md              # @AGENTS.md 연결 파일
└─ PROGRESS.md            # 개발 진행 기록
```

---

## 주의사항

- 본 프로젝트는 **개인 연구 및 오프라인 커스텀 콘텐츠 플레이 목적**으로 제작되었습니다.
- 온라인 랭킹/기록 오염 방지를 위해 기본 설정에서 `OfflineMode=1`, `BlockSave=1`이 활성화되어 있습니다.
- 게임 원본 에셋, 바이너리, IL2CPP 덤프 및 디컴파일 코드는 본 저장소에 포함되지 않습니다.
