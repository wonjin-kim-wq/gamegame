using UnityEngine;
using UnityEngine.UI;

namespace Chameleon.Inputs
{
    /// <summary>
    /// 인게임 HUD의 입력 요소를 한 곳에 모아 두는 허브.
    /// 플레이어 프리팹은 런타임에 PhotonNetwork.Instantiate 로 생성되므로
    /// 씬에 미리 놓인 UI를 인스펙터로 직접 연결할 수 없다. → 허브 싱글톤으로 찾아 쓴다.
    ///
    /// [씬 구성] 01_Classroom 씬의 HUD Canvas에 이 스크립트를 붙이고 각 항목을 연결.
    /// </summary>
    public class MobileInputHub : MonoBehaviour
    {
        public static MobileInputHub Instance { get; private set; }

        [Header("이동 / 시점")]
        public FloatingJoystick Joystick;
        public TouchLookArea LookArea;

        [Header("액션 버튼")]
        [Tooltip("바라보는 곳의 색을 빨아들이는 '변색' 버튼")]
        public Button BtnCamouflage;
        [Tooltip("술래 전용 '잡기' 버튼")]
        public Button BtnCatch;
        [Tooltip("팔레트 열기 버튼 (원하는 색 직접 선택)")]
        public Button BtnPalette;

        [Header("표시용")]
        public GameObject SeekerOnlyGroup;   // 술래에게만 보일 UI 묶음
        public GameObject HiderOnlyGroup;    // 도망자에게만 보일 UI 묶음

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>역할에 따라 버튼 세트를 전환한다.</summary>
        public void ApplyRoleUI(bool isSeeker)
        {
            if (SeekerOnlyGroup) SeekerOnlyGroup.SetActive(isSeeker);
            if (HiderOnlyGroup) HiderOnlyGroup.SetActive(!isSeeker);
        }
    }
}
