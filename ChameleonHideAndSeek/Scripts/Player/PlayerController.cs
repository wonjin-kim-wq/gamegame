using Chameleon.Inputs;
using Photon.Pun;
using UnityEngine;

// ※ 네임스페이스를 Chameleon.Player 로 하면 Photon.Realtime.Player 타입과 충돌해
//    CS0118 오류가 난다. 반드시 Characters 등 다른 이름을 쓸 것.
namespace Chameleon.Characters
{
    /// <summary>
    /// 플레이어 3D 이동 / 회전 / 카메라 제어.
    ///
    /// 네트워크 동기화 전략
    ///  - 위치·회전 : PhotonTransformView(또는 Classic) 컴포넌트가 자동 처리. 여기서 직접 보내지 않는다.
    ///  - 애니메이션: PhotonAnimatorView 사용 권장.
    ///  - 내 캐릭터만 조작: photonView.IsMine 검사 후 원격 캐릭터의 입력/카메라/콜라이더를 끈다.
    ///
    /// [필수 컴포넌트] PhotonView, PhotonTransformView, CharacterController
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviourPun
    {
        [Header("이동")]
        [SerializeField] private float moveSpeed = 3.2f;          // 초등학생 조작감 기준: 너무 빠르면 멀미
        [SerializeField] private float sprintMultiplier = 1.5f;   // 술래는 조금 더 빠르게
        [SerializeField] private float rotationSmooth = 12f;      // 캐릭터가 이동 방향으로 도는 속도
        [SerializeField] private float gravity = -18f;
        [SerializeField] private float groundedStick = -2f;       // 바닥에 붙여두기 위한 미세 하강값

        [Header("카메라 (자식 오브젝트)")]
        [SerializeField] private Transform cameraPivot;           // 캐릭터 머리 위 빈 오브젝트
        [SerializeField] private Camera playerCamera;             // cameraPivot의 자식
        [SerializeField] private AudioListener audioListener;
        [SerializeField] private float minPitch = -35f;
        [SerializeField] private float maxPitch = 55f;

        [Header("연출")]
        [SerializeField] private Animator animator;               // 없으면 비워둬도 동작
        [SerializeField] private string animSpeedParam = "Speed";

        // 외부(GameManager 등)에서 이동을 잠글 때 사용. 술래 카운트다운 중 도망자만 움직이게 하는 등.
        public bool InputLocked { get; set; }

        /// <summary>다른 스크립트(ChameleonColorSync, SeekerCatcher)가 레이 기준으로 쓸 카메라</summary>
        public Camera PlayerCamera => playerCamera;

        private CharacterController cc;
        private float yaw;        // 좌우 회전(캐릭터 본체)
        private float pitch;      // 상하 회전(카메라 피벗)
        private float verticalVelocity;
        private float speedMultiplier = 1f;

        private MobileInputHub Hub => MobileInputHub.Instance;

        private void Awake()
        {
            cc = GetComponent<CharacterController>();

            // ★ 핵심: 내 캐릭터가 아니면 카메라/오디오리스너를 반드시 끈다.
            //   (안 끄면 씬에 카메라가 여러 개 생겨 화면이 깨지고 AudioListener 경고가 뜬다)
            bool isMine = photonView.IsMine;

            if (playerCamera != null) playerCamera.enabled = isMine;
            if (audioListener != null) audioListener.enabled = isMine;

            if (!isMine)
            {
                // 원격 캐릭터는 CharacterController로 직접 움직이지 않는다.
                // PhotonTransformView가 Transform을 덮어쓰므로 서로 충돌하지 않게 비활성화.
                cc.enabled = false;
                enabled = false;   // Update 자체를 돌리지 않음 → 성능 이득
                return;
            }

            yaw = transform.eulerAngles.y;
        }

        private void Start()
        {
            if (!photonView.IsMine) return;

            // 태블릿 화면 꺼짐 방지 + 프레임 안정화 (수업 중 화면 잠김 방지)
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Application.targetFrameRate = 60;
        }

        private void Update()
        {
            if (!photonView.IsMine) return;

            if (InputLocked)
            {
                ApplyGravityOnly();
                SetAnimSpeed(0f);
                return;
            }

            HandleLook();
            HandleMove();
        }

        // ────────────────────────────────────────────────────────────────
        // 시점 회전 : 오른쪽 화면 드래그
        // ────────────────────────────────────────────────────────────────
        private void HandleLook()
        {
            Vector2 look = Vector2.zero;

            if (Hub != null && Hub.LookArea != null)
                look = Hub.LookArea.LookDelta;

#if UNITY_EDITOR || UNITY_STANDALONE
            // 에디터 테스트용: 마우스 우클릭 드래그로 시점 회전
            if (UnityEngine.Input.GetMouseButton(1))
                look += new Vector2(UnityEngine.Input.GetAxis("Mouse X"), UnityEngine.Input.GetAxis("Mouse Y")) * 3f;
#endif

            yaw += look.x;
            pitch = Mathf.Clamp(pitch - look.y, minPitch, maxPitch);

            // 좌우는 캐릭터 본체가 돈다 (→ PhotonTransformView가 회전까지 동기화)
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            // 상하는 카메라 피벗만 돈다 (동기화 불필요 — 시야 방향은 색 추출 시 RPC로 결과만 전달)
            if (cameraPivot != null)
                cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        // ────────────────────────────────────────────────────────────────
        // 이동 : 왼쪽 플로팅 조이스틱
        // ────────────────────────────────────────────────────────────────
        private void HandleMove()
        {
            Vector2 input = Vector2.zero;

            if (Hub != null && Hub.Joystick != null)
                input = Hub.Joystick.Direction;

#if UNITY_EDITOR || UNITY_STANDALONE
            // 에디터 테스트용: WASD
            if (input.sqrMagnitude < 0.01f)
                input = new Vector2(UnityEngine.Input.GetAxisRaw("Horizontal"),
                                    UnityEngine.Input.GetAxisRaw("Vertical"));
#endif

            // 카메라(=캐릭터)가 바라보는 방향 기준으로 이동 방향 계산
            Vector3 forward = transform.forward;
            Vector3 right = transform.right;
            Vector3 move = (forward * input.y + right * input.x);
            if (move.sqrMagnitude > 1f) move.Normalize();

            float speed = moveSpeed * speedMultiplier;
            Vector3 velocity = move * speed;

            // 중력 처리
            if (cc.isGrounded && verticalVelocity < 0f) verticalVelocity = groundedStick;
            verticalVelocity += gravity * Time.deltaTime;
            velocity.y = verticalVelocity;

            cc.Move(velocity * Time.deltaTime);

            SetAnimSpeed(move.magnitude);
        }

        private void ApplyGravityOnly()
        {
            if (cc.isGrounded && verticalVelocity < 0f) verticalVelocity = groundedStick;
            verticalVelocity += gravity * Time.deltaTime;
            cc.Move(new Vector3(0f, verticalVelocity, 0f) * Time.deltaTime);
        }

        private void SetAnimSpeed(float v)
        {
            if (animator != null) animator.SetFloat(animSpeedParam, v, 0.1f, Time.deltaTime);
        }

        // ────────────────────────────────────────────────────────────────
        // 외부 제어용 API
        // ────────────────────────────────────────────────────────────────
        /// <summary>술래는 조금 빠르게, 잡힌 사람은 0으로 — GameManager가 호출.</summary>
        public void SetSpeedMultiplier(float m) => speedMultiplier = Mathf.Max(0f, m);
        public void ApplySprintForSeeker() => speedMultiplier = sprintMultiplier;

        /// <summary>스폰 지점으로 즉시 이동 (CharacterController는 켜진 상태로 순간이동하면 튕기므로 잠깐 끈다)</summary>
        public void Teleport(Vector3 pos, Quaternion rot)
        {
            bool wasEnabled = cc.enabled;
            cc.enabled = false;
            transform.SetPositionAndRotation(pos, rot);
            yaw = rot.eulerAngles.y;
            cc.enabled = wasEnabled;
        }
    }
}
