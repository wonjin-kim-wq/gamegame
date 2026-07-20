using Chameleon.Game;
using Chameleon.Inputs;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Chameleon.UI
{
    /// <summary>
    /// 인게임 HUD. GameManager 이벤트를 구독해 타이머/역할/안내문구를 표시한다.
    ///
    /// [씬 구성] HUD Canvas에 부착. MobileInputHub와 같은 오브젝트에 둬도 무방.
    /// </summary>
    public class GameHUD : MonoBehaviour
    {
        [Header("상단 정보")]
        [SerializeField] private TMP_Text txtTimer;        // "02:35"
        [SerializeField] private TMP_Text txtRole;         // "🦎 카멜레온" / "👀 술래"
        [SerializeField] private TMP_Text txtRemaining;    // "남은 카멜레온 3명"

        [Header("중앙 안내")]
        [SerializeField] private GameObject announceRoot;
        [SerializeField] private TMP_Text txtAnnounce;
        [SerializeField] private float announceDuration = 3f;

        [Header("술래 눈가림 (Hiding 단계)")]
        [SerializeField] private Image blindOverlay;       // 검은 전체화면 이미지

        [Header("조준점")]
        [SerializeField] private GameObject crosshair;

        [Header("팔레트")]
        [SerializeField] private ColorPaletteUI palette;

        [Header("결과")]
        [SerializeField] private GameObject panelResult;
        [SerializeField] private TMP_Text txtResult;
        [SerializeField] private Button btnBackToLobby;

        private float announceTimer;
        private GameManager GM => GameManager.Instance;

        private void Start()
        {
            if (GM != null)
            {
                GM.OnTimerTick += UpdateTimer;
                GM.OnPhaseChanged += UpdatePhase;
                GM.OnAnnounce += ShowAnnounce;
                GM.OnMyRoleAssigned += UpdateRole;
            }

            var hub = MobileInputHub.Instance;
            if (hub != null && hub.BtnPalette != null && palette != null)
                hub.BtnPalette.onClick.AddListener(palette.Open);

            if (btnBackToLobby != null)
                btnBackToLobby.onClick.AddListener(() => PhotonNetwork.LeaveRoom());

            if (announceRoot) announceRoot.SetActive(false);
            if (panelResult) panelResult.SetActive(false);
            if (blindOverlay) blindOverlay.enabled = false;
        }

        private void OnDestroy()
        {
            if (GM == null) return;
            GM.OnTimerTick -= UpdateTimer;
            GM.OnPhaseChanged -= UpdatePhase;
            GM.OnAnnounce -= ShowAnnounce;
            GM.OnMyRoleAssigned -= UpdateRole;
        }

        private void Update()
        {
            if (announceRoot != null && announceRoot.activeSelf)
            {
                announceTimer -= Time.deltaTime;
                if (announceTimer <= 0f) announceRoot.SetActive(false);
            }
        }

        private void UpdateTimer(int seconds)
        {
            if (txtTimer == null) return;
            txtTimer.text = $"{seconds / 60:00}:{seconds % 60:00}";
            // 10초 이하면 빨갛게
            txtTimer.color = seconds <= 10 ? new Color(1f, 0.3f, 0.3f) : Color.white;
        }

        private void UpdateRole(PlayerRole role)
        {
            if (txtRole != null)
                txtRole.text = role == PlayerRole.Seeker ? "👀 술래" : "🦎 카멜레온";

            var hub = MobileInputHub.Instance;
            if (hub != null) hub.ApplyRoleUI(role == PlayerRole.Seeker);

            if (crosshair != null) crosshair.SetActive(true);
        }

        private void UpdatePhase(GamePhase phase)
        {
            if (txtRemaining != null && GM != null)
                txtRemaining.text = $"남은 카멜레온 {GM.CountRemainingHiders()}명";

            // 술래는 숨는 시간 동안 화면이 가려진다
            if (blindOverlay != null)
                blindOverlay.enabled = (phase == GamePhase.Hiding && GM != null && GM.AmISeeker);

            if (phase == GamePhase.Result && panelResult != null)
            {
                panelResult.SetActive(true);
                if (txtResult != null && GM != null)
                    txtResult.text = GM.CountRemainingHiders() > 0
                        ? "카멜레온 승리! 🦎"
                        : "술래 승리! 🎉";
            }
        }

        private void ShowAnnounce(string msg)
        {
            if (announceRoot == null || txtAnnounce == null) { Debug.Log(msg); return; }
            txtAnnounce.text = msg;
            announceRoot.SetActive(true);
            announceTimer = announceDuration;
        }
    }
}
