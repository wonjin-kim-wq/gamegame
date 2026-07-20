using System.Collections.Generic;
using System.Text;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chameleon.Network
{
    /// <summary>
    /// 포톤 마스터 서버 연결 / 초대코드 기반 방 생성·참가 / 로비 인원 관리 / 게임 시작을 담당하는 싱글톤 매니저.
    /// - 씬 구성: [00_Lobby] 씬에 빈 GameObject 하나 만들고 이 스크립트를 붙인다.
    /// - UI는 LobbyUI.cs 가 담당하고, 이 클래스는 "네트워크 상태"만 책임진다. (관심사 분리)
    /// </summary>
    public class NetworkManager : MonoBehaviourPunCallbacks
    {
        public static NetworkManager Instance { get; private set; }

        // ────────────────────────────────────────────────────────────────
        // 인스펙터 설정값
        // ────────────────────────────────────────────────────────────────
        [Header("게임 설정")]
        [Tooltip("한 방(교실)에 들어갈 수 있는 최대 인원. 초등 교실 기준 8~12명 권장")]
        [SerializeField] private byte maxPlayersPerRoom = 8;

        [Tooltip("게임 시작에 필요한 최소 인원 (테스트 시 1로 낮춰도 됨)")]
        [SerializeField] private int minPlayersToStart = 2;

        [Tooltip("게임 시작 시 로드할 씬 이름 (Build Settings에 반드시 등록)")]
        [SerializeField] private string gameSceneName = "01_Classroom";

        [Tooltip("초대 코드 자리수 (4~6 권장)")]
        [Range(4, 6)]
        [SerializeField] private int inviteCodeLength = 5;

        [Header("버전 관리")]
        [Tooltip("게임 버전이 다르면 서로 매칭되지 않는다. 앱 업데이트 시 반드시 올릴 것.")]
        [SerializeField] private string gameVersion = "1.0";

        // ────────────────────────────────────────────────────────────────
        // 상태 & 이벤트 (UI 스크립트가 구독해서 화면을 갱신한다)
        // ────────────────────────────────────────────────────────────────
        public enum NetState { Disconnected, Connecting, ConnectedToMaster, JoiningRoom, InRoom }
        public NetState State { get; private set; } = NetState.Disconnected;

        /// <summary>연결/방 상태가 바뀔 때 호출. UI에서 버튼 활성화 처리에 사용.</summary>
        public event System.Action<NetState> OnStateChanged;
        /// <summary>방 인원이 변할 때 호출 (현재인원, 최대인원)</summary>
        public event System.Action<int, int> OnRoomPlayerCountChanged;
        /// <summary>에러 메시지 (방 없음, 방 가득참 등)를 UI에 띄우기 위한 이벤트</summary>
        public event System.Action<string> OnNetworkError;

        /// <summary>현재 방의 초대 코드. 로비 화면에 크게 띄워서 친구에게 불러주게 한다.</summary>
        public string CurrentInviteCode => PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.Name : string.Empty;

        /// <summary>내가 방장(마스터 클라이언트)인가?</summary>
        public bool IsHost => PhotonNetwork.IsMasterClient;

        /// <summary>게임 시작 버튼을 눌러도 되는 상태인가?</summary>
        public bool CanStartGame =>
            PhotonNetwork.InRoom &&
            PhotonNetwork.IsMasterClient &&
            PhotonNetwork.CurrentRoom.PlayerCount >= minPlayersToStart;

        // 초대 코드에 쓰는 문자 집합.
        // 헷갈리는 글자(0/O, 1/I/L)는 일부러 제외했다. 초등학생이 코드를 불러줄 때 오타를 줄이기 위함.
        private const string CODE_CHARS = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

        // 방 커스텀 프로퍼티 키
        public const string ROOM_PROP_STATE = "gs";   // 게임 진행 상태 (로비/인게임)

        // ────────────────────────────────────────────────────────────────
        // 라이프사이클
        // ────────────────────────────────────────────────────────────────
        private void Awake()
        {
            // 싱글톤 + 씬 전환에도 살아남기
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            // ★ 중요: 마스터 클라이언트가 LoadLevel을 호출하면 모든 클라이언트가 같은 씬으로 자동 이동한다.
            //   이걸 켜두지 않으면 방장만 게임 씬으로 넘어가고 나머지는 로비에 남는다.
            PhotonNetwork.AutomaticallySyncScene = true;
        }

        private void Start()
        {
            Connect();
        }

        // ────────────────────────────────────────────────────────────────
        // 1) 마스터 서버 연결
        // ────────────────────────────────────────────────────────────────
        /// <summary>
        /// PhotonServerSettings 에셋에 입력해 둔 App ID / 지역(Region) 설정으로 마스터 서버에 접속한다.
        /// (Window > Photon Unity Networking > Highlight Server Settings 에서 App ID 입력)
        /// </summary>
        public void Connect()
        {
            if (PhotonNetwork.IsConnected)
            {
                SetState(NetState.ConnectedToMaster);
                return;
            }

            SetState(NetState.Connecting);

            PhotonNetwork.GameVersion = gameVersion;
            // 닉네임이 비어 있으면 임시 이름 부여 (로비 UI에서 학생이 직접 입력하게 해도 좋다)
            if (string.IsNullOrEmpty(PhotonNetwork.NickName))
                PhotonNetwork.NickName = "학생" + Random.Range(100, 1000);

            PhotonNetwork.ConnectUsingSettings();
        }

        public override void OnConnectedToMaster()
        {
            Debug.Log("[Net] 마스터 서버 연결 완료. 지역: " + PhotonNetwork.CloudRegion);
            SetState(NetState.ConnectedToMaster);
        }

        public override void OnDisconnected(DisconnectCause cause)
        {
            Debug.LogWarning("[Net] 연결 끊김: " + cause);
            SetState(NetState.Disconnected);
            OnNetworkError?.Invoke("서버 연결이 끊어졌어요. 다시 시도해 주세요.");
        }

        // ────────────────────────────────────────────────────────────────
        // 2) 방 만들기 (Host) — 무작위 초대 코드 생성
        // ────────────────────────────────────────────────────────────────
        /// <summary>
        /// 무작위 초대 코드를 만들어 방 생성한다.
        /// 코드가 이미 존재해서 실패하면(OnCreateRoomFailed) 자동으로 다른 코드로 재시도한다.
        /// </summary>
        public void CreateRoomWithInviteCode()
        {
            if (State != NetState.ConnectedToMaster)
            {
                OnNetworkError?.Invoke("아직 서버에 연결 중이에요. 잠시만 기다려 주세요.");
                return;
            }

            SetState(NetState.JoiningRoom);

            string code = GenerateInviteCode(inviteCodeLength);

            RoomOptions options = new RoomOptions
            {
                MaxPlayers = maxPlayersPerRoom,
                IsVisible = false,   // 랜덤 매칭 목록에 노출 X → 초대 코드로만 입장 (교실 수업용으로 안전)
                IsOpen = true,
                CleanupCacheOnLeave = true,
                // 로비 화면에서 방 상태를 보여주기 위한 커스텀 프로퍼티
                CustomRoomProperties = new ExitGames.Client.Photon.Hashtable
                {
                    { ROOM_PROP_STATE, (int)0 } // 0 = 로비 대기중, 1 = 게임중
                },
                CustomRoomPropertiesForLobby = new[] { ROOM_PROP_STATE }
            };

            Debug.Log($"[Net] 방 생성 시도. 초대 코드: {code}");
            PhotonNetwork.CreateRoom(code, options, TypedLobby.Default);
        }

        public override void OnCreateRoomFailed(short returnCode, string message)
        {
            Debug.LogWarning($"[Net] 방 생성 실패({returnCode}): {message} → 다른 코드로 재시도");
            // 코드 중복(ErrorCode.GameIdAlreadyExists = 32766)인 경우가 대부분이므로 즉시 재시도
            SetState(NetState.ConnectedToMaster);
            CreateRoomWithInviteCode();
        }

        // ────────────────────────────────────────────────────────────────
        // 3) 방 참가하기 (Client) — 초대 코드 입력
        // ────────────────────────────────────────────────────────────────
        /// <summary>
        /// 사용자가 입력한 초대 코드로 방에 입장한다.
        /// 대소문자 구분 없이 입력해도 되도록 대문자로 정규화한다.
        /// </summary>
        public void JoinRoomWithInviteCode(string rawCode)
        {
            if (State != NetState.ConnectedToMaster)
            {
                OnNetworkError?.Invoke("아직 서버에 연결 중이에요. 잠시만 기다려 주세요.");
                return;
            }

            string code = NormalizeCode(rawCode);

            if (code.Length < 4)
            {
                OnNetworkError?.Invoke("초대 코드를 정확히 입력해 주세요.");
                return;
            }

            SetState(NetState.JoiningRoom);
            Debug.Log("[Net] 방 참가 시도: " + code);
            PhotonNetwork.JoinRoom(code);
        }

        public override void OnJoinRoomFailed(short returnCode, string message)
        {
            Debug.LogWarning($"[Net] 방 참가 실패({returnCode}): {message}");
            SetState(NetState.ConnectedToMaster);

            // ErrorCode.GameDoesNotExist = 32758 / GameFull = 32765 / GameClosed = 32764
            switch (returnCode)
            {
                case ErrorCode.GameDoesNotExist:
                    OnNetworkError?.Invoke("그런 초대 코드는 없어요. 다시 확인해 주세요!");
                    break;
                case ErrorCode.GameFull:
                    OnNetworkError?.Invoke("방이 가득 찼어요.");
                    break;
                case ErrorCode.GameClosed:
                    OnNetworkError?.Invoke("이미 게임이 시작된 방이에요.");
                    break;
                default:
                    OnNetworkError?.Invoke("방에 들어가지 못했어요. 다시 시도해 주세요.");
                    break;
            }
        }

        // ────────────────────────────────────────────────────────────────
        // 4) 로비(방 안) 인원 관리
        // ────────────────────────────────────────────────────────────────
        public override void OnJoinedRoom()
        {
            SetState(NetState.InRoom);
            Debug.Log($"[Net] 입장 완료. 코드={PhotonNetwork.CurrentRoom.Name}, " +
                      $"인원={PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers}, " +
                      $"방장={PhotonNetwork.IsMasterClient}");
            RaisePlayerCount();
        }

        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            Debug.Log("[Net] 입장: " + newPlayer.NickName);
            RaisePlayerCount();
        }

        public override void OnPlayerLeftRoom(Player otherPlayer)
        {
            Debug.Log("[Net] 퇴장: " + otherPlayer.NickName);
            RaisePlayerCount();
        }

        public override void OnMasterClientSwitched(Player newMasterClient)
        {
            // 방장이 나가면 다음 사람이 자동으로 방장이 된다. UI 갱신 필요.
            Debug.Log("[Net] 새 방장: " + newMasterClient.NickName);
            RaisePlayerCount();
        }

        /// <summary>방 안의 플레이어 목록 (UI 리스트 표시용)</summary>
        public List<Player> GetPlayersInRoom()
        {
            var list = new List<Player>();
            if (!PhotonNetwork.InRoom) return list;
            foreach (var p in PhotonNetwork.PlayerList) list.Add(p);
            return list;
        }

        public void LeaveRoom()
        {
            if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom();
        }

        public override void OnLeftRoom()
        {
            SetState(NetState.ConnectedToMaster);
            // 게임 씬에서 나왔다면 로비 씬으로 복귀
            if (SceneManager.GetActiveScene().name != "00_Lobby")
                SceneManager.LoadScene("00_Lobby");
        }

        // ────────────────────────────────────────────────────────────────
        // 5) 게임 시작 (방장 전용)
        // ────────────────────────────────────────────────────────────────
        /// <summary>
        /// 방장만 호출 가능. 방을 잠그고(추가 입장 차단) 전원을 게임 씬으로 이동시킨다.
        /// AutomaticallySyncScene = true 이므로 PhotonNetwork.LoadLevel 하나면 전원이 따라온다.
        /// </summary>
        public void StartGame()
        {
            if (!PhotonNetwork.IsMasterClient)
            {
                OnNetworkError?.Invoke("게임 시작은 방장만 할 수 있어요.");
                return;
            }
            if (PhotonNetwork.CurrentRoom.PlayerCount < minPlayersToStart)
            {
                OnNetworkError?.Invoke($"{minPlayersToStart}명 이상 모여야 시작할 수 있어요.");
                return;
            }

            // 게임 시작 후 난입 방지
            PhotonNetwork.CurrentRoom.IsOpen = false;
            PhotonNetwork.CurrentRoom.IsVisible = false;
            PhotonNetwork.CurrentRoom.SetCustomProperties(
                new ExitGames.Client.Photon.Hashtable { { ROOM_PROP_STATE, 1 } });

            Debug.Log("[Net] 게임 시작 → " + gameSceneName);
            PhotonNetwork.LoadLevel(gameSceneName);
        }

        // ────────────────────────────────────────────────────────────────
        // 유틸
        // ────────────────────────────────────────────────────────────────
        private string GenerateInviteCode(int length)
        {
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
                sb.Append(CODE_CHARS[Random.Range(0, CODE_CHARS.Length)]);
            return sb.ToString();
        }

        /// <summary>입력값 정규화: 공백/하이픈 제거 + 대문자 변환</summary>
        public static string NormalizeCode(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            return raw.Trim().Replace(" ", "").Replace("-", "").ToUpperInvariant();
        }

        private void SetState(NetState s)
        {
            if (State == s) return;
            State = s;
            OnStateChanged?.Invoke(s);
        }

        private void RaisePlayerCount()
        {
            if (!PhotonNetwork.InRoom) return;
            OnRoomPlayerCountChanged?.Invoke(
                PhotonNetwork.CurrentRoom.PlayerCount,
                PhotonNetwork.CurrentRoom.MaxPlayers);
        }
    }
}
