# 카멜레온 숨바꼭질 — Unity + PUN2 세팅 가이드

## 1. 스크립트 구조

```
Assets/Scripts/
├── Network/
│   ├── NetworkManager.cs      ★ 포톤 연결, 초대코드 방 생성/참가, 게임 시작
│   └── LobbyUI.cs               로비 화면 UI (버튼/입력창/인원 목록)
├── Input/
│   ├── FloatingJoystick.cs      왼쪽 화면 플로팅 조이스틱 (이동)
│   ├── TouchLookArea.cs         오른쪽 화면 드래그 (시점 회전)
│   └── MobileInputHub.cs        런타임 생성 플레이어가 HUD를 찾기 위한 허브
├── Player/
│   ├── PlayerController.cs    ★ 3D 이동/회전, IsMine 검증, 카메라 제어
│   ├── ChameleonColorSync.cs  ★ Raycast 색 추출 + [PunRPC] 색상 동기화
│   └── SeekerCatcher.cs         술래의 SphereCast 잡기 판정
├── Game/
│   ├── GameManager.cs           역할 배정, 단계 전환, 서버시간 타이머, 잡힘 판정
│   └── PlayerSpawner.cs         PhotonNetwork.Instantiate 로 내 캐릭터 생성
└── UI/
    ├── GameHUD.cs               타이머/역할/안내문구/결과 패널
    └── ColorPaletteUI.cs        원하는 색 직접 선택
```

### 핵심 클래스 요약

| 클래스 | 상속 | 책임 |
|---|---|---|
| `NetworkManager` | `MonoBehaviourPunCallbacks` | 마스터 서버 연결, 초대코드 Create/Join, 방장 게임 시작 |
| `GameManager` | `MonoBehaviourPunCallbacks` | 술래/도망자 배정, Hiding→Seeking→Result 단계, 잡힘 승인 |
| `PlayerController` | `MonoBehaviourPun` | 조이스틱 이동, 시점 회전, `IsMine` 아니면 카메라·입력 차단 |
| `ChameleonColorSync` | `MonoBehaviourPun`, `IPunObservable` | 색 추출 + `[PunRPC] RPC_ApplyColor` 전파 |
| `SeekerCatcher` | `MonoBehaviourPun` | 술래 터치 공격 → 마스터에 판정 요청 |

---

## 2. 설계상 중요한 판단 3가지

**① 타이머는 `PhotonNetwork.ServerTimestamp` 기준**
각 기기의 `Time.time`으로 재면 태블릿마다 초가 어긋납니다. 방 프로퍼티에 "종료 시각(ms)"만 저장하고 각자 빼서 계산하면 완벽히 일치합니다.

**② 색 동기화에 `AllBuffered`를 쓰지 않음**
변색을 30번 하면 버퍼가 30개 쌓여서 늦게 들어온 학생이 과거 색을 순차 재생하게 됩니다. `RpcTarget.All` + `IPunObservable`(최신 상태만 전송) 조합이 정답입니다.

**③ 잡힘 판정은 마스터 클라이언트 승인제**
각자 로컬에서 "잡았다"를 확정하면 두 술래가 동시에 같은 학생을 잡는 경합이 생깁니다. `RpcTarget.MasterClient`로 신청 → 마스터가 검증 후 확정합니다.

---

### 네임스페이스 주의 (실제로 자주 터지는 함정)

- 플레이어 스크립트 네임스페이스는 `Chameleon.Player`가 **아니라** `Chameleon.Characters`입니다. `Player`로 두면 `Photon.Realtime.Player`와 충돌해 CS0118이 납니다.
- `ExitGames.Client.Photon.Hashtable`은 `System.Collections.Hashtable`과 겹치므로 `using Hashtable = ExitGames.Client.Photon.Hashtable;` 별칭이 필수입니다. `GameManager.cs`에 이미 넣어뒀습니다.

---

## 3. Photon 설정

1. [Photon Dashboard](https://dashboard.photonengine.com)에서 **PUN 앱 생성** → App ID 복사
2. Asset Store에서 **PUN 2 - FREE** 임포트
3. `Window > Photon Unity Networking > Highlight Server Settings`
   - **App Id PUN**: 복사한 App ID
   - **Fixed Region**: `kr` (한국 서버 — 교실 내 지연 최소화)
   - **PUN Logging**: `Errors` (배포 시)
4. `File > Build Settings`에 씬 등록: `00_Lobby`(0번), `01_Classroom`(1번)

> 무료 플랜은 동시접속 20명(CCU)까지입니다. 한 반(약 25명)이 동시에 쓰면 초과하므로, 반을 나눠 돌리거나 유료 플랜을 확인하세요.

---

## 4. 인스펙터 연결

### 00_Lobby 씬
| 오브젝트 | 컴포넌트 | 연결 |
|---|---|---|
| `_NetworkManager` (빈 GO) | `NetworkManager` | Max Players 8, Min Players 2, Game Scene Name = `01_Classroom` |
| `Canvas` | `LobbyUI` | Panel 3개 + 버튼/InputField/Text 전부 드래그 연결 |

### 플레이어 프리팹 — **반드시 `Assets/Resources/Prefabs/` 안에 저장**
```
ChameleonPlayer                     ← 루트
├─ [PhotonView]                     Observed Components에 ChameleonColorSync 추가
│                                   Synchronization: Unreliable On Change
├─ [PhotonTransformView]            ✔ Position  ✔ Rotation  (Scale 불필요)
├─ [CharacterController]            Height 1.6 / Radius 0.3 / Center (0,0.8,0)
├─ [PlayerController]               Camera Pivot, Player Camera, Audio Listener 연결
├─ [ChameleonColorSync]             Target Renderers = 몸/옷 렌더러
│                                   Color Property = URP는 `_BaseColor`, Built-in은 `_Color`
├─ [SeekerCatcher]                  Player Layer / Obstacle Layer 지정
└─ CameraPivot (Y≈1.5)
     └─ MainCamera + AudioListener
```

### 01_Classroom 씬
| 오브젝트 | 컴포넌트 | 비고 |
|---|---|---|
| `_GameManager` | `GameManager` + `PhotonView` | Hiding 30초 / Seeking 180초 |
| `_Spawner` | `PlayerSpawner` | Prefab Path = `Prefabs/ChameleonPlayer`, 스폰 포인트 배열 |
| `HUD Canvas` | `MobileInputHub`, `GameHUD`, `ColorPaletteUI` | 조이스틱·버튼·텍스트 연결 |
| `EventSystem` | — | **필수** (없으면 터치 입력 전부 무시됨) |

### 레이어 설정
`Player`, `Wall`, `Floor`, `Furniture` 4개를 만들고
- `ChameleonColorSync.paintableLayers` = Wall + Floor + Furniture (Player 제외)
- `SeekerCatcher.playerLayer` = Player
- `SeekerCatcher.obstacleLayer` = Wall + Furniture

---

## 5. 변색 정확도 관련 (중요)

레이캐스트로 **텍스처의 실제 픽셀 색**을 뽑으려면 두 조건이 모두 필요합니다.

1. 대상 오브젝트가 **MeshCollider** (Convex 체크 해제) — BoxCollider면 `hit.textureCoord`가 항상 (0,0)
2. 텍스처 임포트 설정에서 **Read/Write Enabled** ✔

조건이 안 맞으면 코드가 자동으로 머티리얼 색으로 폴백하므로 크래시는 나지 않지만, 색이 뭉뚱그려집니다.

> 다만 Read/Write Enabled는 텍스처 메모리를 2배로 씁니다. iPad에서 교실 맵 전체에 켜면 무거워지니, **벽·바닥·주요 가구에만** 켜는 걸 권합니다.

---

## 6. 모바일 빌드 체크리스트

- `Player Settings > Other Settings`
  - Color Space: **Linear**
  - Graphics API: iOS `Metal`, Android `Vulkan + OpenGLES3`
  - Target minimum: iOS 13 / Android API 26
- `Quality Settings`: 태블릿용 프로파일에서 그림자 Hard Only, 텍스처 Half Res
- `Application.targetFrameRate = 60` — `PlayerController.Start()`에 이미 포함
- `Screen.sleepTimeout = NeverSleep` — 수업 중 화면 잠김 방지, 역시 포함

---

## 7. 에디터 테스트 방법

캐릭터 조작은 태블릿 없이도 테스트할 수 있게 키보드 폴백을 넣어뒀습니다.

| 키 | 동작 |
|---|---|
| WASD | 이동 |
| 마우스 우클릭 드래그 | 시점 회전 |
| E | 바라보는 곳 색으로 변색 |
| Space | 잡기 (술래일 때) |

멀티 테스트는 `Build And Run`으로 빌드본 하나 + 에디터 하나를 동시에 띄우고, 같은 초대 코드로 접속하면 됩니다. `NetworkManager`의 Min Players를 1로 낮추면 혼자서도 시작 가능합니다.

---

## 8. 다음에 붙이면 좋을 것

- **잡힌 학생 관전 모드**: 현재는 이동만 잠기므로, 남은 친구 시점으로 카메라를 옮겨주면 소외감이 줄어듭니다
- **선생님용 관리자 뷰**: 교실 전체를 내려다보며 진행 상황을 보는 별도 역할
- **음성 힌트 대신 시각 힌트**: 술래가 가까워지면 도망자 화면 테두리가 붉게 — 교실이 시끄러워도 작동
