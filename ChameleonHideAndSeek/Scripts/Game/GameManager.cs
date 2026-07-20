using System.Collections;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

// ★ System.Collections.Hashtable 과 이름이 겹치므로 반드시 별칭을 지정해야 컴파일된다.
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace Chameleon.Game
{
    /// <summary>플레이어 역할</summary>
    public enum PlayerRole { None = 0, Hider = 1, Seeker = 2 }

    /// <summary>게임 진행 단계</summary>
    public enum GamePhase { Waiting = 0, Hiding = 1, Seeking = 2, Result = 3 }

    /// <summary>
    /// 게임 전체 흐름(역할 배정 → 숨는 시간 → 찾는 시간 → 결과)을 관리한다.
    /// 권한 구조: 마스터 클라이언트만 상태를 바꾸고, 나머지는 결과만 받는다. (치팅·불일치 방지)
    ///
    /// 상태 저장 방식
    ///  - 역할        : Player.CustomProperties["role"]  → 재접속/늦은 참가에도 유지됨
    ///  - 잡힘 여부   : Player.CustomProperties["caught"]
    ///  - 게임 단계   : Room.CustomProperties["phase"], ["endAt"] (서버 시간 기준 종료 시각)
    ///
    /// ★ 타이머를 각자 로컬 시간으로 재면 기기마다 어긋난다.
    ///   PhotonNetwork.ServerTimestamp(모든 클라이언트 공통) 기준으로 계산해야 정확하다.
    ///
    /// [씬 구성] 01_Classroom 씬의 빈 오브젝트에 부착. PhotonView 필요.
    /// </summary>
    public class GameManager : MonoBehaviourPunCallbacks
    {
        public static GameManager Instance { get; private set; }

        [Header("규칙 설정")]
        [Tooltip("도망자가 숨는 시간(초). 이 동안 술래는 움직일 수 없다.")]
        [SerializeField] private int hidingSeconds = 30;

        [Tooltip("술래가 찾는 시간(초)")]
        [SerializeField] private int seekingSeconds = 180;

        [Tooltip("인원 몇 명당 술래 1명으로 할지 (예: 4 → 8명이면 술래 2명)")]
        [SerializeField] private int playersPerSeeker = 5;

        // 커스텀 프로퍼티 키 (짧게 써야 패킷이 작다)
        public const string P_ROLE = "rl";
        public const string P_CAUGHT = "ct";
        public const string R_PHASE = "ph";
        public const string R_END_AT = "ea";   // ServerTimestamp(ms) 기준 종료 시각

        // ── 이벤트 (HUD가 구독) ──────────────────────────────────────────
        public event System.Action<GamePhase> OnPhaseChanged;
        public event System.Action<int> OnTimerTick;          // 남은 초
        public event System.Action<string> OnAnnounce;        // 중앙 안내 문구
        public event System.Action<PlayerRole> OnMyRoleAssigned;

        public GamePhase Phase { get; private set; } = GamePhase.Waiting;
        public int RemainingSeconds { get; private set; }

        /// <summary>내 역할</summary>
        public PlayerRole MyRole => GetRole(PhotonNetwork.LocalPlayer);
        public bool AmISeeker => MyRole == PlayerRole.Seeker;

        private int lastBroadcastSecond = -1;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            // 마스터 클라이언트만 게임을 개시한다
            if (PhotonNetwork.IsMasterClient)
                StartCoroutine(CoBeginMatch());
            else
                SyncFromRoomProperties(); // 늦게 들어온 경우 현재 상태를 즉시 반영
        }

        // ────────────────────────────────────────────────────────────────
        // 1) 역할 배정 (마스터 전용)
        // ────────────────────────────────────────────────────────────────
        private IEnumerator CoBeginMatch()
        {
            // 모든 클라이언트가 씬 로드를 마칠 시간을 준다
            yield return new WaitForSeconds(1.0f);

            AssignRoles();
            SetPhase(GamePhase.Hiding, hidingSeconds);
        }

        /// <summary>
        /// 인원수에 따라 술래를 무작위로 뽑고, 각 플레이어의 CustomProperties에 역할을 기록한다.
        /// CustomProperties는 방 전체에 자동 동기화되므로 별도 RPC가 필요 없다.
        /// </summary>
        private void AssignRoles()
        {
            var players = new List<Player>(PhotonNetwork.PlayerList);

            // Fisher–Yates 셔플
            for (int i = players.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (players[i], players[j]) = (players[j], players[i]);
            }

            int seekerCount = Mathf.Max(1, players.Count / playersPerSeeker);
            seekerCount = Mathf.Min(seekerCount, players.Count - 1); // 최소 1명은 도망자

            for (int i = 0; i < players.Count; i++)
            {
                PlayerRole role = i < seekerCount ? PlayerRole.Seeker : PlayerRole.Hider;
                players[i].SetCustomProperties(new Hashtable
                {
                    { P_ROLE, (int)role },
                    { P_CAUGHT, false }
                });
            }

            Debug.Log($"[Game] 역할 배정 완료. 술래 {seekerCount}명 / 전체 {players.Count}명");
        }

        public static PlayerRole GetRole(Player p)
        {
            if (p != null && p.CustomProperties.TryGetValue(P_ROLE, out object v))
                return (PlayerRole)(int)v;
            return PlayerRole.None;
        }

        public static bool IsCaught(Player p)
        {
            if (p != null && p.CustomProperties.TryGetValue(P_CAUGHT, out object v))
                return (bool)v;
            return false;
        }

        // ────────────────────────────────────────────────────────────────
        // 2) 단계 전환 (마스터 전용)
        // ────────────────────────────────────────────────────────────────
        private void SetPhase(GamePhase phase, int durationSeconds)
        {
            if (!PhotonNetwork.IsMasterClient) return;

            // ServerTimestamp는 int(ms)이며 모든 클라이언트가 동일한 값을 본다
            int endAt = PhotonNetwork.ServerTimestamp + durationSeconds * 1000;

            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable
            {
                { R_PHASE, (int)phase },
                { R_END_AT, endAt }
            });
        }

        /// <summary>방 프로퍼티가 바뀌면 모든 클라이언트에서 호출된다 → 여기서 상태를 반영</summary>
        public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
        {
            SyncFromRoomProperties();
        }

        private void SyncFromRoomProperties()
        {
            var props = PhotonNetwork.CurrentRoom?.CustomProperties;
            if (props == null) return;

            if (props.TryGetValue(R_PHASE, out object p))
            {
                var newPhase = (GamePhase)(int)p;
                if (newPhase != Phase)
                {
                    Phase = newPhase;
                    ApplyPhaseLocally(newPhase);
                    OnPhaseChanged?.Invoke(newPhase);
                }
            }
        }

        /// <summary>각 클라이언트가 자기 화면/입력에 단계를 반영한다.</summary>
        private void ApplyPhaseLocally(GamePhase phase)
        {
            var local = FindLocalPlayerObject();

            switch (phase)
            {
                case GamePhase.Hiding:
                    OnMyRoleAssigned?.Invoke(MyRole);
                    if (AmISeeker)
                    {
                        // 술래는 숨는 시간 동안 움직일 수 없고 화면이 가려진다
                        if (local != null) local.InputLocked = true;
                        OnAnnounce?.Invoke("눈을 감고 기다려요! 곧 찾으러 갑니다");
                    }
                    else
                    {
                        if (local != null) local.InputLocked = false;
                        OnAnnounce?.Invoke("빨리 숨어요! 주변 색으로 변신할 수 있어요");
                    }
                    break;

                case GamePhase.Seeking:
                    if (local != null)
                    {
                        local.InputLocked = false;
                        if (AmISeeker) local.ApplySprintForSeeker();
                    }
                    OnAnnounce?.Invoke(AmISeeker ? "찾으러 출발!" : "술래가 눈을 떴어요!");
                    break;

                case GamePhase.Result:
                    if (local != null) local.InputLocked = true;
                    OnAnnounce?.Invoke(BuildResultText());
                    break;
            }
        }

        // ────────────────────────────────────────────────────────────────
        // 3) 타이머 (모든 클라이언트가 서버 시간 기준으로 동일하게 계산)
        // ────────────────────────────────────────────────────────────────
        private void Update()
        {
            if (Phase == GamePhase.Waiting || Phase == GamePhase.Result) return;

            var props = PhotonNetwork.CurrentRoom?.CustomProperties;
            if (props == null || !props.TryGetValue(R_END_AT, out object e)) return;

            int endAt = (int)e;
            int remainMs = endAt - PhotonNetwork.ServerTimestamp;
            RemainingSeconds = Mathf.Max(0, Mathf.CeilToInt(remainMs / 1000f));

            // 1초 단위로만 UI 이벤트를 쏜다
            if (RemainingSeconds != lastBroadcastSecond)
            {
                lastBroadcastSecond = RemainingSeconds;
                OnTimerTick?.Invoke(RemainingSeconds);
            }

            // 시간 종료 판정은 마스터만 수행 (중복 실행 방지)
            if (remainMs <= 0 && PhotonNetwork.IsMasterClient)
            {
                if (Phase == GamePhase.Hiding) SetPhase(GamePhase.Seeking, seekingSeconds);
                else if (Phase == GamePhase.Seeking) SetPhase(GamePhase.Result, 0);
            }
        }

        // ────────────────────────────────────────────────────────────────
        // 4) 잡힘 처리
        // ────────────────────────────────────────────────────────────────
        /// <summary>
        /// SeekerCatcher가 도망자를 터치했을 때 호출한다.
        /// 판정 권한을 마스터에게 몰아주기 위해 MasterClient로 RPC를 보낸다.
        /// </summary>
        public void ReportCatch(int caughtActorNumber, int seekerActorNumber)
        {
            photonView.RPC(nameof(RPC_OnCatch), RpcTarget.MasterClient, caughtActorNumber, seekerActorNumber);
        }

        [PunRPC]
        private void RPC_OnCatch(int caughtActor, int seekerActor, PhotonMessageInfo info)
        {
            if (!PhotonNetwork.IsMasterClient) return;
            if (Phase != GamePhase.Seeking) return;

            Player caught = GetPlayerByActor(caughtActor);
            Player seeker = GetPlayerByActor(seekerActor);
            if (caught == null || seeker == null) return;

            // 유효성 검사: 술래가 도망자를, 아직 안 잡힌 사람만 잡을 수 있다
            if (GetRole(seeker) != PlayerRole.Seeker) return;
            if (GetRole(caught) != PlayerRole.Hider) return;
            if (IsCaught(caught)) return;

            caught.SetCustomProperties(new Hashtable { { P_CAUGHT, true } });

            photonView.RPC(nameof(RPC_AnnounceCatch), RpcTarget.All, caught.NickName, seeker.NickName);

            // 도망자가 전부 잡혔으면 즉시 종료
            if (CountRemainingHiders() <= 0)
                SetPhase(GamePhase.Result, 0);
        }

        [PunRPC]
        private void RPC_AnnounceCatch(string caughtName, string seekerName)
        {
            OnAnnounce?.Invoke($"{seekerName} 님이 {caughtName} 님을 찾았어요!");

            // 잡힌 사람이 나라면 관전 모드로 (조작 잠금 + 반투명)
            if (PhotonNetwork.NickName == caughtName)
            {
                var local = FindLocalPlayerObject();
                if (local != null) local.SetSpeedMultiplier(0f);
            }
        }

        public int CountRemainingHiders()
        {
            int n = 0;
            foreach (var p in PhotonNetwork.PlayerList)
                if (GetRole(p) == PlayerRole.Hider && !IsCaught(p)) n++;
            return n;
        }

        public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
        {
            // 잡힘 상태가 바뀌면 HUD의 "남은 인원" 표시를 갱신
            if (changedProps.ContainsKey(P_CAUGHT) || changedProps.ContainsKey(P_ROLE))
                OnPhaseChanged?.Invoke(Phase);
        }

        private string BuildResultText()
        {
            int remain = CountRemainingHiders();
            return remain > 0
                ? $"시간 종료! 카멜레온 {remain}명이 끝까지 숨었어요 🦎"
                : "술래 승리! 모두 찾았어요 🎉";
        }

        // ────────────────────────────────────────────────────────────────
        // 유틸
        // ────────────────────────────────────────────────────────────────
        private static Player GetPlayerByActor(int actor)
        {
            foreach (var p in PhotonNetwork.PlayerList)
                if (p.ActorNumber == actor) return p;
            return null;
        }

        private Chameleon.Characters.PlayerController FindLocalPlayerObject()
        {
            foreach (var pc in Object.FindObjectsOfType<Chameleon.Characters.PlayerController>())
            {
                var pv = pc.GetComponent<PhotonView>();
                if (pv != null && pv.IsMine) return pc;
            }
            return null;
        }

        /// <summary>방장이 나가면 남은 사람 중 새 방장이 진행을 이어받는다.</summary>
        public override void OnMasterClientSwitched(Player newMasterClient)
        {
            if (PhotonNetwork.IsMasterClient)
                Debug.Log("[Game] 진행 권한을 넘겨받았습니다.");
        }
    }
}
