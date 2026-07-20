using System.Text;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Chameleon.Network
{
    /// <summary>
    /// 로비(메인) 화면 UI 컨트롤러.
    /// NetworkManager의 이벤트를 구독해서 화면만 갱신한다. 네트워크 로직은 여기에 두지 않는다.
    ///
    /// [씬 구성 예시 — 00_Lobby]
    ///   Canvas
    ///    ├ Panel_Main      : [방 만들기] [방 참가하기] 버튼
    ///    ├ Panel_JoinCode  : 초대코드 입력 InputField + [입장] 버튼
    ///    └ Panel_Room      : 초대코드 표시 + 인원 목록 + [게임 시작] 버튼
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        [Header("패널")]
        [SerializeField] private GameObject panelMain;
        [SerializeField] private GameObject panelJoinCode;
        [SerializeField] private GameObject panelRoom;

        [Header("메인 패널")]
        [SerializeField] private Button btnCreateRoom;
        [SerializeField] private Button btnOpenJoin;
        [SerializeField] private TMP_InputField inputNickname;
        [SerializeField] private TMP_Text txtStatus;      // "서버 연결 중..." 등

        [Header("참가 패널")]
        [SerializeField] private TMP_InputField inputInviteCode;
        [SerializeField] private Button btnJoinRoom;
        [SerializeField] private Button btnBackFromJoin;

        [Header("룸(대기실) 패널")]
        [SerializeField] private TMP_Text txtInviteCode;   // 크게! 아이들이 서로 불러줌
        [SerializeField] private TMP_Text txtPlayerCount;  // "3 / 8"
        [SerializeField] private TMP_Text txtPlayerList;   // 닉네임 목록
        [SerializeField] private Button btnStartGame;
        [SerializeField] private Button btnLeaveRoom;

        [Header("공통")]
        [SerializeField] private GameObject toastRoot;
        [SerializeField] private TMP_Text txtToast;

        private NetworkManager Net => NetworkManager.Instance;
        private float toastTimer;

        private void Start()
        {
            // 초대 코드 입력창: 대문자 자동 변환 + 6자 제한
            if (inputInviteCode != null)
            {
                inputInviteCode.characterLimit = 6;
                inputInviteCode.onValueChanged.AddListener(v =>
                {
                    string up = NetworkManager.NormalizeCode(v);
                    if (up != v) inputInviteCode.text = up;
                });
            }

            btnCreateRoom.onClick.AddListener(OnClickCreateRoom);
            btnOpenJoin.onClick.AddListener(() => ShowPanel(panelJoinCode));
            btnJoinRoom.onClick.AddListener(OnClickJoinRoom);
            btnBackFromJoin.onClick.AddListener(() => ShowPanel(panelMain));
            btnStartGame.onClick.AddListener(() => Net.StartGame());
            btnLeaveRoom.onClick.AddListener(() => Net.LeaveRoom());

            Net.OnStateChanged += HandleStateChanged;
            Net.OnRoomPlayerCountChanged += HandlePlayerCountChanged;
            Net.OnNetworkError += ShowToast;

            HandleStateChanged(Net.State);
            if (toastRoot) toastRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Net == null) return;
            Net.OnStateChanged -= HandleStateChanged;
            Net.OnRoomPlayerCountChanged -= HandlePlayerCountChanged;
            Net.OnNetworkError -= ShowToast;
        }

        private void Update()
        {
            // 방장 여부 / 인원 조건이 바뀔 수 있으므로 시작 버튼은 매 프레임 가볍게 갱신
            if (panelRoom != null && panelRoom.activeSelf)
                btnStartGame.interactable = Net.CanStartGame;

            if (toastRoot != null && toastRoot.activeSelf)
            {
                toastTimer -= Time.deltaTime;
                if (toastTimer <= 0f) toastRoot.SetActive(false);
            }
        }

        // ── 버튼 핸들러 ────────────────────────────────────────────────
        private void OnClickCreateRoom()
        {
            ApplyNickname();
            Net.CreateRoomWithInviteCode();
        }

        private void OnClickJoinRoom()
        {
            ApplyNickname();
            Net.JoinRoomWithInviteCode(inputInviteCode.text);
        }

        private void ApplyNickname()
        {
            if (inputNickname != null && !string.IsNullOrWhiteSpace(inputNickname.text))
                PhotonNetwork.NickName = inputNickname.text.Trim();
        }

        // ── 상태 반영 ──────────────────────────────────────────────────
        private void HandleStateChanged(NetworkManager.NetState s)
        {
            switch (s)
            {
                case NetworkManager.NetState.Disconnected:
                    txtStatus.text = "서버에 연결되지 않았어요";
                    SetMainButtons(false);
                    ShowPanel(panelMain);
                    break;

                case NetworkManager.NetState.Connecting:
                    txtStatus.text = "서버에 연결하는 중...";
                    SetMainButtons(false);
                    ShowPanel(panelMain);
                    break;

                case NetworkManager.NetState.ConnectedToMaster:
                    txtStatus.text = "연결 완료! 방을 만들거나 코드를 입력하세요";
                    SetMainButtons(true);
                    if (panelRoom.activeSelf) ShowPanel(panelMain);
                    break;

                case NetworkManager.NetState.JoiningRoom:
                    txtStatus.text = "방으로 들어가는 중...";
                    SetMainButtons(false);
                    break;

                case NetworkManager.NetState.InRoom:
                    ShowPanel(panelRoom);
                    txtInviteCode.text = Net.CurrentInviteCode;
                    RefreshPlayerList();
                    break;
            }
        }

        private void HandlePlayerCountChanged(int current, int max)
        {
            txtPlayerCount.text = $"{current} / {max}";
            RefreshPlayerList();
        }

        private void RefreshPlayerList()
        {
            if (!PhotonNetwork.InRoom) return;
            var sb = new StringBuilder();
            foreach (var p in Net.GetPlayersInRoom())
                sb.AppendLine(p.IsMasterClient ? $"👑 {p.NickName}" : $"　 {p.NickName}");
            txtPlayerList.text = sb.ToString();
        }

        private void SetMainButtons(bool on)
        {
            btnCreateRoom.interactable = on;
            btnOpenJoin.interactable = on;
            btnJoinRoom.interactable = on;
        }

        private void ShowPanel(GameObject target)
        {
            panelMain.SetActive(target == panelMain);
            panelJoinCode.SetActive(target == panelJoinCode);
            panelRoom.SetActive(target == panelRoom);
        }

        private void ShowToast(string msg)
        {
            if (toastRoot == null) { Debug.LogWarning(msg); return; }
            txtToast.text = msg;
            toastRoot.SetActive(true);
            toastTimer = 2.5f;
        }
    }
}
