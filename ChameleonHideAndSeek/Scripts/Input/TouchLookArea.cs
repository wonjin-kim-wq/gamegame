using UnityEngine;
using UnityEngine.EventSystems;

namespace Chameleon.Inputs
{
    /// <summary>
    /// 화면 오른쪽 절반을 드래그하면 카메라(시야)를 회전시키는 터치 영역.
    /// 조이스틱과 동시에 멀티터치로 동작한다.
    ///
    /// [계층 구조]
    ///   LookArea (Image, alpha 0, Raycast Target ✔, 화면 오른쪽 절반) ← 이 스크립트 부착
    /// </summary>
    public class TouchLookArea : MonoBehaviour, IDragHandler, IPointerDownHandler, IPointerUpHandler
    {
        [Tooltip("드래그 1픽셀당 회전 각도")]
        [Range(0.02f, 1f)][SerializeField] private float sensitivity = 0.18f;

        /// <summary>이번 프레임의 드래그 델타 (x=좌우 회전, y=상하 회전)</summary>
        public Vector2 LookDelta { get; private set; }
        public bool IsPressed { get; private set; }

        private Vector2 accumulated;

        public void OnPointerDown(PointerEventData eventData) => IsPressed = true;

        public void OnDrag(PointerEventData eventData)
        {
            accumulated += eventData.delta * sensitivity;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            IsPressed = false;
            accumulated = Vector2.zero;
        }

        private void LateUpdate()
        {
            // 한 프레임에 한 번만 소비되도록 LateUpdate에서 비운다.
            LookDelta = accumulated;
            accumulated = Vector2.zero;
        }
    }
}
