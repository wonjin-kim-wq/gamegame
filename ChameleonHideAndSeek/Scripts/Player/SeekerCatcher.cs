using Chameleon.Game;
using Chameleon.Inputs;
using Photon.Pun;
using UnityEngine;

namespace Chameleon.Characters
{
    /// <summary>
    /// 술래의 '잡기(터치 공격)' 판정.
    ///
    /// 판정 방식: 카메라 중앙에서 SphereCast를 쏜다.
    ///  - Raycast(선)보다 SphereCast(굵은 선)가 태블릿 터치 조작에서 훨씬 관대해서
    ///    초등학생도 답답함 없이 잡을 수 있다.
    ///  - 최종 승인은 마스터 클라이언트가 하므로(GameManager.ReportCatch),
    ///    여기서의 판정은 "신청"에 가깝다.
    ///
    /// [부착 위치] 플레이어 프리팹 (PlayerController와 같은 오브젝트)
    /// </summary>
    public class SeekerCatcher : MonoBehaviourPun
    {
        [Header("판정")]
        [SerializeField] private float catchRange = 2.2f;
        [SerializeField] private float catchRadius = 0.5f;
        [Tooltip("플레이어 레이어만 지정")]
        [SerializeField] private LayerMask playerLayer;
        [Tooltip("벽 뒤의 사람을 잡지 못하게 막을 장애물 레이어")]
        [SerializeField] private LayerMask obstacleLayer;

        [Header("쿨타임")]
        [SerializeField] private float cooldown = 1.0f;

        [Header("연출")]
        [SerializeField] private Animator animator;
        [SerializeField] private string attackTrigger = "Attack";
        [SerializeField] private ParticleSystem swingVfx;

        private PlayerController controller;
        private float lastTryTime = -99f;

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
        }

        private void Start()
        {
            if (!photonView.IsMine) { enabled = false; return; }

            var hub = MobileInputHub.Instance;
            if (hub != null && hub.BtnCatch != null)
                hub.BtnCatch.onClick.AddListener(TryCatch);
        }

        private void OnDestroy()
        {
            if (!photonView.IsMine) return;
            var hub = MobileInputHub.Instance;
            if (hub != null && hub.BtnCatch != null)
                hub.BtnCatch.onClick.RemoveListener(TryCatch);
        }

#if UNITY_EDITOR || UNITY_STANDALONE
        private void Update()
        {
            // 에디터 테스트용: 스페이스바로 잡기
            if (photonView.IsMine && UnityEngine.Input.GetKeyDown(KeyCode.Space)) TryCatch();
        }
#endif

        /// <summary>UI [잡기] 버튼에서 호출</summary>
        public void TryCatch()
        {
            if (!photonView.IsMine) return;
            if (Time.time - lastTryTime < cooldown) return;

            var gm = GameManager.Instance;
            if (gm == null || gm.Phase != GamePhase.Seeking) return;
            if (!gm.AmISeeker) return;   // 술래만 잡을 수 있다

            lastTryTime = Time.time;

            // 휘두르는 연출은 모두에게 보여준다
            photonView.RPC(nameof(RPC_PlaySwing), RpcTarget.All);

            Camera cam = controller != null ? controller.PlayerCamera : Camera.main;
            if (cam == null) return;

            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            if (!Physics.SphereCast(ray, catchRadius, out RaycastHit hit, catchRange,
                                    playerLayer, QueryTriggerInteraction.Ignore))
                return;

            // 자기 자신은 제외
            var targetView = hit.collider.GetComponentInParent<PhotonView>();
            if (targetView == null || targetView == photonView) return;

            // 벽 너머는 무효: 나 → 대상 사이에 장애물이 있으면 취소
            Vector3 origin = cam.transform.position;
            Vector3 targetPos = targetView.transform.position + Vector3.up * 1.0f;
            Vector3 dir = targetPos - origin;
            if (Physics.Raycast(origin, dir.normalized, dir.magnitude - 0.2f, obstacleLayer))
                return;

            // 마스터에게 판정 요청
            GameManager.Instance.ReportCatch(targetView.OwnerActorNr, photonView.OwnerActorNr);
        }

        [PunRPC]
        private void RPC_PlaySwing()
        {
            if (animator != null && !string.IsNullOrEmpty(attackTrigger))
                animator.SetTrigger(attackTrigger);
            if (swingVfx != null) swingVfx.Play();
        }

        private void OnDrawGizmosSelected()
        {
            // 에디터에서 잡기 범위를 시각적으로 확인
            var cam = GetComponentInChildren<Camera>();
            if (cam == null) return;
            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(cam.transform.position + cam.transform.forward * catchRange, catchRadius);
        }
    }
}
