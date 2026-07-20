using Chameleon.Characters;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

namespace Chameleon.UI
{
    /// <summary>
    /// 도망자가 원하는 색을 직접 고를 수 있는 팔레트.
    /// 팔레트 버튼(Image)의 color 값을 그대로 캐릭터 색으로 적용한다.
    ///
    /// [씬 구성] HUD Canvas > Panel_Palette 아래에 색깔 Button들을 배치하고
    ///          이 스크립트의 colorButtons 배열에 연결.
    /// </summary>
    public class ColorPaletteUI : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Button[] colorButtons;
        [SerializeField] private Button btnClose;

        private void Start()
        {
            foreach (var btn in colorButtons)
            {
                if (btn == null) continue;
                Button captured = btn;
                captured.onClick.AddListener(() => Pick(captured.targetGraphic.color));
            }

            if (btnClose != null) btnClose.onClick.AddListener(Close);
            if (panel != null) panel.SetActive(false);
        }

        public void Open() { if (panel) panel.SetActive(true); }
        public void Close() { if (panel) panel.SetActive(false); }

        private void Pick(Color c)
        {
            // 내 로컬 플레이어를 찾아 색을 적용
            var myView = FindLocalPlayerView();
            if (myView == null) return;

            var sync = myView.GetComponent<ChameleonColorSync>();
            if (sync != null) sync.SetColorManually(c);

            Close();
        }

        private PhotonView FindLocalPlayerView()
        {
            foreach (var pc in Object.FindObjectsOfType<PlayerController>())
            {
                var pv = pc.GetComponent<PhotonView>();
                if (pv != null && pv.IsMine) return pv;
            }
            return null;
        }
    }
}
