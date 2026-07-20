using Photon.Pun;
using UnityEngine;

namespace Chameleon.Game
{
    /// <summary>
    /// 게임 씬 진입 시 내 캐릭터를 네트워크로 생성한다.
    ///
    /// ★ PhotonNetwork.Instantiate 는 프리팹이 반드시
    ///   Assets/Resources/ 폴더 안에 있어야 한다. (경로에 확장자 없이 이름만)
    ///
    /// [씬 구성] 01_Classroom 씬의 빈 오브젝트에 부착.
    /// </summary>
    public class PlayerSpawner : MonoBehaviourPunCallbacks
    {
        [Header("프리팹")]
        [Tooltip("Assets/Resources 안의 프리팹 이름 (예: \"Prefabs/ChameleonPlayer\")")]
        [SerializeField] private string playerPrefabPath = "Prefabs/ChameleonPlayer";

        [Header("스폰 위치")]
        [Tooltip("교실 안 스폰 포인트들. 비워두면 원점 주변에 랜덤 배치")]
        [SerializeField] private Transform[] hiderSpawnPoints;
        [SerializeField] private Transform[] seekerSpawnPoints;
        [SerializeField] private float randomRadius = 4f;

        private GameObject myAvatar;

        private void Start()
        {
            if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom)
            {
                Debug.LogError("[Spawn] 방에 들어와 있지 않습니다. 로비 씬부터 실행하세요.");
                return;
            }

            SpawnMyPlayer();
        }

        private void SpawnMyPlayer()
        {
            // 역할은 마스터가 곧 배정하므로, 스폰 시점엔 아직 None일 수 있다.
            // → 위치만 일단 잡고, 역할 확정 후 GameManager가 필요 시 Teleport 시킨다.
            PlayerRole role = GameManager.GetRole(PhotonNetwork.LocalPlayer);
            GetSpawnPose(role, out Vector3 pos, out Quaternion rot);

            myAvatar = PhotonNetwork.Instantiate(playerPrefabPath, pos, rot);
            Debug.Log($"[Spawn] 내 캐릭터 생성 완료: {myAvatar.name}");
        }

        private void GetSpawnPose(PlayerRole role, out Vector3 pos, out Quaternion rot)
        {
            Transform[] points = role == PlayerRole.Seeker ? seekerSpawnPoints : hiderSpawnPoints;

            if (points != null && points.Length > 0)
            {
                // ActorNumber로 인덱스를 나눠 서로 겹치지 않게 배치
                int idx = (PhotonNetwork.LocalPlayer.ActorNumber - 1) % points.Length;
                pos = points[idx].position;
                rot = points[idx].rotation;
            }
            else
            {
                Vector2 c = Random.insideUnitCircle * randomRadius;
                pos = new Vector3(c.x, 1f, c.y);
                rot = Quaternion.identity;
            }
        }

        /// <summary>씬을 떠날 때 내 캐릭터 정리 (Photon이 자동 정리하지만 명시적으로)</summary>
        public override void OnLeftRoom()
        {
            if (myAvatar != null) PhotonNetwork.Destroy(myAvatar);
        }
    }
}
