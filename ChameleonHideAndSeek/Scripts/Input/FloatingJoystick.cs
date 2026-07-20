using UnityEngine;
using UnityEngine.EventSystems;

namespace Chameleon.Inputs
{
    /// <summary>
    /// 태블릿용 플로팅 조이스틱.
    /// 화면(지정 영역)을 손가락으로 누른 지점에 스틱이 나타나고, 드래그한 방향/세기를 Direction으로 반환한다.
    /// 외부 에셋 없이 uGUI 이벤트만으로 동작한다.
    ///
    /// [계층 구조]
    ///   JoystickArea (Image, alpha 0, Raycast Target ✔, 화면 왼쪽 절반 크기)  ← 이 스크립트 부착
    ///     └ Background (Image, 원형)
    ///          └ Handle (Image, 원형, 작게)
    /// </summary>
    public class FloatingJoystick : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [Header("참조")]
        [SerializeField] private RectTransform background;  // 조이스틱 배경 원
        [SerializeField] private RectTransform handle;      // 손잡이

        [Header("설정")]
        [Tooltip("손잡이가 배경 반지름의 몇 %까지 움직일 수 있는지")]
        [Range(0.3f, 1f)][SerializeField] private float handleRange = 0.8f;

        [Tooltip("이 값보다 작은 입력은 0으로 취급 (손 떨림 방지)")]
        [Range(0f, 0.4f)][SerializeField] private float deadZone = 0.1f;

        [Tooltip("터치를 떼면 조이스틱을 숨길지 여부")]
        [SerializeField] private bool hideOnRelease = true;

        /// <summary>-1 ~ 1 범위의 2D 입력값. x=좌우, y=앞뒤</summary>
        public Vector2 Direction { get; private set; } = Vector2.zero;
        public float Horizontal => Direction.x;
        public float Vertical => Direction.y;
        /// <summary>현재 조이스틱을 잡고 있는가</summary>
        public bool IsPressed { get; private set; }

        private RectTransform areaRect;   // 터치를 받는 영역
        private Canvas canvas;
        private Camera uiCamera;          // Screen Space - Camera / World Space 대응

        private void Awake()
        {
            areaRect = GetComponent<RectTransform>();
            canvas = GetComponentInParent<Canvas>();

            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                uiCamera = canvas.worldCamera;

            if (hideOnRelease && background != null)
                background.gameObject.SetActive(false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            IsPressed = true;

            // 누른 위치로 조이스틱 배경을 이동시킨다 (플로팅 방식)
            if (background != null)
            {
                background.gameObject.SetActive(true);
                background.position = eventData.position;
                // Screen Space - Camera 인 경우 z 보정
                if (uiCamera != null) background.position = uiCamera.ScreenToWorldPoint(
                    new Vector3(eventData.position.x, eventData.position.y, canvas.planeDistance));
            }

            OnDrag(eventData); // 누른 즉시 입력 반영
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (background == null) return;

            // 배경 원의 로컬 좌표계로 터치 지점을 변환
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                background, eventData.position, uiCamera, out Vector2 local);

            float radius = background.sizeDelta.x * 0.5f;
            Vector2 raw = local / (radius * handleRange);

            // 원 밖으로 나가면 정규화해서 최대 1로 제한
            Direction = raw.magnitude > 1f ? raw.normalized : raw;

            // 데드존 처리
            if (Direction.magnitude < deadZone) Direction = Vector2.zero;

            if (handle != null)
                handle.anchoredPosition = Direction * radius * handleRange;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            IsPressed = false;
            Direction = Vector2.zero;

            if (handle != null) handle.anchoredPosition = Vector2.zero;
            if (hideOnRelease && background != null) background.gameObject.SetActive(false);
        }
    }
}
