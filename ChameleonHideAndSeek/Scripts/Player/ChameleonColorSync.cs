using System.Collections.Generic;
using Chameleon.Inputs;
using Photon.Pun;
using UnityEngine;

namespace Chameleon.Characters
{
    /// <summary>
    /// 카멜레온 변색 로직 + 네트워크 동기화.
    ///
    /// 동작 흐름
    ///  1) 도망자가 [변색] 버튼을 누른다.
    ///  2) 카메라 중앙에서 Raycast → 맞은 벽/바닥의 색을 추출한다.
    ///     - MeshCollider + Read/Write Enabled 텍스처면 → 실제 픽셀 색(정확)
    ///     - 아니면 → 머티리얼의 baseColor (근사)
    ///  3) [PunRPC] RPC_ApplyColor 로 모든 클라이언트에 전파한다.
    ///  4) 늦게 들어온 사람에게도 보이도록, 새 플레이어 입장 시 소유자가 현재 색을 다시 보낸다.
    ///
    /// ※ 왜 AllBuffered를 쓰지 않는가?
    ///    변색을 수십 번 하면 버퍼가 계속 쌓여 신규 접속자가 과거 색을 순차 재생하게 된다.
    ///    "최신 상태만" 필요하므로 재전송 방식이 훨씬 가볍고 정확하다.
    ///
    /// [필수 컴포넌트] PhotonView (PlayerController와 같은 오브젝트)
    /// </summary>
    public class ChameleonColorSync : MonoBehaviourPun, IPunObservable
    {
        [Header("변색 대상")]
        [Tooltip("색이 바뀔 렌더러들 (피부, 옷 등). 비워두면 자식 SkinnedMeshRenderer를 자동 수집)")]
        [SerializeField] private Renderer[] targetRenderers;

        [Tooltip("URP/Lit = _BaseColor, Built-in Standard = _Color")]
        [SerializeField] private string colorPropertyName = "_BaseColor";

        [Header("레이캐스트")]
        [SerializeField] private float rayDistance = 6f;
        [Tooltip("색을 빨아들일 수 있는 레이어 (Wall, Floor, Furniture 등). Player 레이어는 제외할 것)")]
        [SerializeField] private LayerMask paintableLayers = ~0;

        [Header("연출")]
        [Tooltip("색이 부드럽게 바뀌는 시간(초). 0이면 즉시 변경")]
        [SerializeField] private float blendDuration = 0.35f;
        [SerializeField] private ParticleSystem camouflageVfx;
        [SerializeField] private AudioSource camouflageSfx;

        [Header("쿨타임")]
        [Tooltip("연타 방지. 네트워크 트래픽 절약에도 중요")]
        [SerializeField] private float cooldown = 0.6f;

        /// <summary>현재 적용된 색 (동기화 대상)</summary>
        public Color CurrentColor { get; private set; } = Color.white;

        private MaterialPropertyBlock mpb;   // 머티리얼 인스턴스 복제를 피해 드로우콜/GC를 아낌
        private int colorPropId;
        private float lastUseTime = -99f;

        // 색 보간용
        private Color blendFrom, blendTo;
        private float blendTimer = -1f;

        private PlayerController controller;

        private void Awake()
        {
            mpb = new MaterialPropertyBlock();
            colorPropId = Shader.PropertyToID(colorPropertyName);
            controller = GetComponent<PlayerController>();

            if (targetRenderers == null || targetRenderers.Length == 0)
            {
                var list = new List<Renderer>();
                list.AddRange(GetComponentsInChildren<SkinnedMeshRenderer>());
                list.AddRange(GetComponentsInChildren<MeshRenderer>());
                targetRenderers = list.ToArray();
            }
        }

        private void Start()
        {
            // 내 캐릭터일 때만 버튼을 연결한다
            if (!photonView.IsMine) return;

            var hub = MobileInputHub.Instance;
            if (hub != null && hub.BtnCamouflage != null)
                hub.BtnCamouflage.onClick.AddListener(TryCamouflageFromView);
        }

        private void OnDestroy()
        {
            if (!photonView.IsMine) return;
            var hub = MobileInputHub.Instance;
            if (hub != null && hub.BtnCamouflage != null)
                hub.BtnCamouflage.onClick.RemoveListener(TryCamouflageFromView);
        }

        private void Update()
        {
            // 색 보간 (모든 클라이언트에서 로컬로 처리 → 네트워크 부담 0)
            if (blendTimer >= 0f)
            {
                blendTimer += Time.deltaTime;
                float t = blendDuration <= 0f ? 1f : Mathf.Clamp01(blendTimer / blendDuration);
                ApplyColorToRenderers(Color.Lerp(blendFrom, blendTo, t));
                if (t >= 1f) blendTimer = -1f;
            }

#if UNITY_EDITOR || UNITY_STANDALONE
            // 에디터 테스트용: E키로 변색
            if (photonView.IsMine && UnityEngine.Input.GetKeyDown(KeyCode.E))
                TryCamouflageFromView();
#endif
        }

        // ────────────────────────────────────────────────────────────────
        // 1) 바라보는 곳의 색 빨아들이기
        // ────────────────────────────────────────────────────────────────
        /// <summary>
        /// 카메라 중앙에서 레이를 쏘아 맞은 표면의 색을 추출하고 전체에 동기화한다.
        /// (UI 버튼 OnClick 또는 코드에서 호출)
        /// </summary>
        public void TryCamouflageFromView()
        {
            if (!photonView.IsMine) return;
            if (Time.time - lastUseTime < cooldown) return;

            Camera cam = controller != null ? controller.PlayerCamera : Camera.main;
            if (cam == null) return;

            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            if (!Physics.Raycast(ray, out RaycastHit hit, rayDistance, paintableLayers, QueryTriggerInteraction.Ignore))
                return; // 아무것도 못 맞췄으면 아무 일도 일어나지 않음

            Color picked = SampleColorFromHit(hit);
            lastUseTime = Time.time;

            // 전체 동기화 (자기 자신 포함 → 로컬 코드 중복 없이 한 경로로 처리)
            photonView.RPC(nameof(RPC_ApplyColor), RpcTarget.All, picked.r, picked.g, picked.b);
        }

        /// <summary>
        /// 팔레트에서 고른 색을 그대로 적용 (원하는 색 직접 칠하기 기능).
        /// UI 팔레트 버튼에서 ColorPaletteUI가 호출한다.
        /// </summary>
        public void SetColorManually(Color c)
        {
            if (!photonView.IsMine) return;
            if (Time.time - lastUseTime < cooldown) return;
            lastUseTime = Time.time;
            photonView.RPC(nameof(RPC_ApplyColor), RpcTarget.All, c.r, c.g, c.b);
        }

        /// <summary>
        /// 히트 지점의 색을 추출한다.
        /// 우선순위: 읽기 가능한 텍스처의 실제 픽셀 → 머티리얼 색 → 흰색
        /// </summary>
        private Color SampleColorFromHit(RaycastHit hit)
        {
            Renderer rend = hit.collider.GetComponentInChildren<Renderer>();
            if (rend == null) return Color.white;

            Material mat = rend.sharedMaterial;
            if (mat == null) return Color.white;

            // (A) 텍스처 픽셀 샘플링 — MeshCollider이고 텍스처가 Read/Write Enabled여야 함
            if (hit.collider is MeshCollider mc && !mc.convex && mat.mainTexture is Texture2D tex)
            {
                try
                {
                    Vector2 uv = hit.textureCoord;
                    uv.Scale(mat.mainTextureScale);
                    uv += mat.mainTextureOffset;

                    Color pixel = tex.GetPixelBilinear(uv.x, uv.y);

                    // 텍스처 색 × 머티리얼 틴트 = 실제 보이는 색에 가깝다
                    Color tint = mat.HasProperty(colorPropId) ? mat.GetColor(colorPropId) : Color.white;
                    return new Color(pixel.r * tint.r, pixel.g * tint.g, pixel.b * tint.b, 1f);
                }
                catch (UnityException)
                {
                    // 텍스처가 Read/Write Disabled 인 경우 예외 → 아래 머티리얼 색으로 폴백
                }
            }

            // (B) 머티리얼 색 폴백
            if (mat.HasProperty(colorPropId)) return mat.GetColor(colorPropId);
            if (mat.HasProperty("_Color")) return mat.GetColor("_Color");
            return Color.white;
        }

        // ────────────────────────────────────────────────────────────────
        // 2) RPC — 모든 클라이언트에 색 적용
        // ────────────────────────────────────────────────────────────────
        /// <summary>
        /// Color 구조체를 그대로 보내면 PUN 커스텀 타입 등록이 필요하므로
        /// float 3개로 쪼개서 보낸다. (패킷도 더 작다)
        /// </summary>
        [PunRPC]
        private void RPC_ApplyColor(float r, float g, float b, PhotonMessageInfo info)
        {
            Color target = new Color(r, g, b, 1f);
            CurrentColor = target;

            // 부드러운 전환 시작
            blendFrom = GetDisplayedColor();
            blendTo = target;
            blendTimer = 0f;

            if (camouflageVfx != null)
            {
                var main = camouflageVfx.main;
                main.startColor = target;
                camouflageVfx.Play();
            }
            if (camouflageSfx != null) camouflageSfx.Play();
        }

        private Color displayedColor = Color.white;

        private Color GetDisplayedColor() => displayedColor;

        private void ApplyColorToRenderers(Color c)
        {
            displayedColor = c;
            foreach (var r in targetRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor(colorPropId, c);
                r.SetPropertyBlock(mpb);
            }
        }

        // ────────────────────────────────────────────────────────────────
        // 3) 늦게 들어온 플레이어 대응
        // ────────────────────────────────────────────────────────────────
        /// <summary>
        /// PhotonView의 Observed Components에 이 스크립트를 추가하면
        /// 신규 접속자에게 "마지막 상태"가 자동으로 전달된다(Observe Option: Reliable Delta Compressed).
        /// RPC 버퍼링 없이 최신 색만 안전하게 복원할 수 있다.
        /// </summary>
        public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
        {
            if (stream.IsWriting)
            {
                stream.SendNext(CurrentColor.r);
                stream.SendNext(CurrentColor.g);
                stream.SendNext(CurrentColor.b);
            }
            else
            {
                float r = (float)stream.ReceiveNext();
                float g = (float)stream.ReceiveNext();
                float b = (float)stream.ReceiveNext();
                Color c = new Color(r, g, b, 1f);

                if (c != CurrentColor)
                {
                    CurrentColor = c;
                    blendFrom = GetDisplayedColor();
                    blendTo = c;
                    blendTimer = 0f;
                }
            }
        }

        /// <summary>게임 리셋 시 원래 색으로</summary>
        public void ResetColor()
        {
            if (!photonView.IsMine) return;
            photonView.RPC(nameof(RPC_ApplyColor), RpcTarget.All, 1f, 1f, 1f);
        }
    }
}
