using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Justitia
{
    public enum ConnectionStage { Offline, Hosting, Connecting, Connected }

    public sealed class LanLobbyConnection : MonoBehaviour
    {
        private const string Protocol = "justitia-lobby-v3";
        private const string KeywordMessage="justitia/keyword";
        private const string StateMessage = "justitia/state";
        private const string ReadyMessage = "justitia/ready";
        private const string SyncMessage = "justitia/sync";
        private const int MaxMessageBytes = 16384;
        public LobbySession Session { get; private set; } = new LobbySession();
        public PlayerRole LocalRole { get; private set; }
        public ConnectionStage Stage { get; private set; }
        public string Message { get; private set; } = "방을 만들거나 Host의 IP로 참가해 주세요.";
        public bool PeerConnected { get; private set; }
        public bool CanReady => (IsDevMode && Stage == ConnectionStage.Connected) || (Stage == ConnectionStage.Connected && PeerConnected && receivedSnapshot);
        public bool IsSteam { get; private set; }
        public bool IsDevMode { get; private set; }
        public bool Available => Stage==ConnectionStage.Offline && manager && !manager.IsListening && !manager.ShutdownInProgress;
        public event Action Changed;
        private NetworkManager manager;
        public NetworkManager Manager => manager;
        public bool IsVoicePeer(ulong id) => PeerConnected && (LocalRole==PlayerRole.Host ? guestId==id : id==NetworkManager.ServerClientId);
        private UnityTransport transport;
        private ulong? guestId;
        private long outgoingSequence, lastGuestSequence;
        private bool receivedSnapshot, stopping;
        private float connectDeadline;

        [Serializable] private sealed class ReadyRequest
        { public string sessionId; public long sequence; public bool ready; }
        [Serializable] private sealed class KeywordRequest
        {public string sessionId;public long sequence;public string keyword;}

        private void Awake()
        {
            Application.runInBackground = true;
            var root = new GameObject("LAN NetworkManager");
            transport = root.AddComponent<UnityTransport>();
            manager = root.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig {
                NetworkTransport=transport, EnableSceneManagement=false, ConnectionApproval=true,
                PlayerPrefab=null, ProtocolVersion=1, ClientConnectionBufferTimeout=10
            };
            transport.ConnectTimeoutMS=1000; transport.MaxConnectAttempts=8; transport.DisconnectTimeoutMS=6000;
            manager.ConnectionApprovalCallback = Approve;
            manager.OnClientConnectedCallback += OnConnected;
            manager.OnClientDisconnectCallback += OnDisconnected;
            manager.OnTransportFailure += OnTransportFailure;
            Session.Changed += SessionChanged;
        }

        public bool Host(ushort port=7777) => Begin(true,"127.0.0.1",port);
        public bool Join(string address, ushort port=7777) => Begin(false,address,port);
        public bool StartSteam(bool host,SteamSocketsTransport steamTransport)=>Begin(host,"127.0.0.1",7777,steamTransport);

        public bool HostDev(ushort port=7777)
        {
            if (Stage != ConnectionStage.Offline) Leave();
            if (!Begin(true, "127.0.0.1", port))
            {
                if (!Begin(true, "127.0.0.1", (ushort)(port + 1))) return false;
            }
            IsDevMode = true;
            PeerConnected = true;
            Stage = ConnectionStage.Connected;
            Message = "개발 전용 방 · 1인 테스트 모드";
            Session.SelectMap(PlayerRole.Host, "court");
            Session.IsDevMode = true;
            Changed?.Invoke();
            return true;
        }

        private bool Begin(bool host,string address,ushort port,NetworkTransport alternate=null)
        {
            if (Stage != ConnectionStage.Offline || manager.IsListening || manager.ShutdownInProgress) return false;
            if (port==0 || !IPAddress.TryParse(address,out var ip) || ip.AddressFamily!=AddressFamily.InterNetwork)
            { Message="올바른 IPv4 주소와 포트를 입력해 주세요.";Changed?.Invoke();return false; }
            stopping=false;guestId=null;PeerConnected=false;receivedSnapshot=host;outgoingSequence=lastGuestSequence=0;
            Session.Changed-=SessionChanged;Session=new LobbySession();Session.Changed+=SessionChanged;
            LocalRole=host?PlayerRole.Host:PlayerRole.Guest;
            IsSteam=alternate!=null;
            Stage=host?ConnectionStage.Hosting:ConnectionStage.Connecting;
            Message=IsSteam ? (host?"Steam 친구 참가 대기 중":"Steam Host에 연결 중…") : host?$"Guest 접속 대기 · UDP {port}":$"{address}:{port} 연결 중…";
            connectDeadline=Time.realtimeSinceStartup+(IsSteam?30:12);
            manager.NetworkConfig.ConnectionData=Encoding.UTF8.GetBytes(Protocol);
            manager.NetworkConfig.NetworkTransport=alternate?alternate:transport;
            manager.NetworkConfig.ClientConnectionBufferTimeout=IsSteam?30:10;
            if(!IsSteam)transport.SetConnectionData(address,port,host?"0.0.0.0":null);
            try
            {
                bool started=host?manager.StartHost():manager.StartClient();
                if(!started){Leave("연결을 시작하지 못했습니다. 포트 사용 여부를 확인해 주세요.");return false;}
                manager.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage,ReceiveState);
                manager.CustomMessagingManager.RegisterNamedMessageHandler(ReadyMessage,ReceiveReady);
                manager.CustomMessagingManager.RegisterNamedMessageHandler(SyncMessage,ReceiveSync);
                manager.CustomMessagingManager.RegisterNamedMessageHandler(KeywordMessage,ReceiveKeyword);
                Changed?.Invoke();return true;
            }
            catch(Exception){Leave("연결을 시작하지 못했습니다. IP와 포트 사용 여부를 확인한 뒤 다시 시도해 주세요.");return false;}
        }

        private void Approve(NetworkManager.ConnectionApprovalRequest request,NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject=false;response.Pending=false;
            if(request.ClientNetworkId==NetworkManager.ServerClientId){response.Approved=true;return;}
            bool compatible=request.Payload!=null && request.Payload.Length==Protocol.Length && Encoding.UTF8.GetString(request.Payload)==Protocol;
            response.Approved=compatible && guestId==null && Session.Phase==LobbyPhase.Preparing && !stopping;
            response.Reason=!compatible?"게임 버전이 다릅니다.":"방이 가득 찼거나 이미 시작되었습니다.";
            if(response.Approved)guestId=request.ClientNetworkId;
        }

        private void OnConnected(ulong id)
        {
            if(stopping)return;
            if(manager.IsHost)
            {
                if(id==NetworkManager.ServerClientId)return;
                if(guestId!=id){manager.DisconnectClient(id);return;}
                PeerConnected=true;Stage=ConnectionStage.Connected;Message="Guest 연결 완료 · 두 플레이어의 준비를 기다립니다.";
                SendState(id);
            }
            else
            {
                PeerConnected=true;Stage=ConnectionStage.Connected;Message="Host 연결 완료 · 상태 동기화 중…";
                Send(SyncMessage,NetworkManager.ServerClientId,"sync");
            }
            Changed?.Invoke();
        }

        private void OnDisconnected(ulong id)
        {
            if(stopping || Stage==ConnectionStage.Offline)return;
            if(manager.IsHost && id!=guestId)return;
            string reason=manager.DisconnectReason;
            bool approvalMessage=reason=="게임 버전이 다릅니다." || reason=="방이 가득 찼거나 이미 시작되었습니다.";
            Leave(approvalMessage?reason:"상대 연결이 종료되어 게임을 중단했습니다. 다시 방을 만들거나 참가해 주세요.");
        }

        private void OnTransportFailure() => Leave(IsSteam?"Steam 연결에 실패했습니다. Steam 로그인과 인터넷 연결을 확인해 주세요.":"네트워크 오류가 발생했습니다. IP와 포트, 방화벽을 확인해 주세요.");

        private void Update()
        {
            if(!IsDevMode && (Stage==ConnectionStage.Connecting || (Stage==ConnectionStage.Connected && !receivedSnapshot)) && Time.realtimeSinceStartup>connectDeadline)
                Leave(IsSteam?"Steam 연결 시간이 초과되었습니다. 친구가 방을 열었는지 확인하고 다시 참가해 주세요.":"연결 시간이 초과되었습니다. Host의 IP와 방화벽을 확인한 뒤 다시 참가해 주세요.");
        }

        public void SetReady(bool ready)
        {
            if(!CanReady || Session.Phase!=LobbyPhase.Preparing)return;
            if(LocalRole==PlayerRole.Host)
            {
                if (IsDevMode) Session.SetDevReady(ready);
                else Session.SetReady(PlayerRole.Host,ready);
            }
            else Send(ReadyMessage,NetworkManager.ServerClientId,JsonUtility.ToJson(new ReadyRequest {
                sessionId=Session.SessionId, sequence=++outgoingSequence, ready=ready
            }));
        }
        public bool SelectMap(string id)
        {return manager && manager.IsHost && LocalRole==PlayerRole.Host && Stage!=ConnectionStage.Offline && Session.SelectMap(PlayerRole.Host,id);}
        public bool SetDevKeywords(string hostKw, string guestKw)
        {
            if (!IsDevMode || !CanReady) return false;
            return Session.SetDevKeywords(hostKw, guestKw);
        }

        public bool SetKeyword(string keyword)
        {
            if(!CanReady || Session.Phase!=LobbyPhase.HostSetup || string.IsNullOrWhiteSpace(keyword) || keyword.Trim().Length>40)return false;
            if(LocalRole==PlayerRole.Host)
            {
                return Session.SetKeyword(PlayerRole.Host,keyword);
            }
            Send(KeywordMessage,NetworkManager.ServerClientId,JsonUtility.ToJson(new KeywordRequest{sessionId=Session.SessionId,sequence=++outgoingSequence,keyword=keyword.Trim()}));return true;
        }
        public bool BeginCaseGeneration()=>CanReady && manager.IsHost && Session.BeginCaseGeneration(PlayerRole.Host);
        public bool CompleteCaseGeneration(string sessionId,string summary,string question,string error="")
        {return CanReady && manager.IsHost && Session.SessionId==sessionId && Session.CompleteCaseGeneration(PlayerRole.Host,summary,question,error);}
        private void ReceiveKeyword(ulong sender,FastBufferReader reader)
        {
            if(!manager.IsHost || sender!=guestId || !PeerConnected || !TryRead(reader,out var json))return;
            try
            {
                var request=JsonUtility.FromJson<KeywordRequest>(json);
                if(request==null || request.sessionId!=Session.SessionId || request.sequence<=lastGuestSequence)return;
                lastGuestSequence=request.sequence;Session.SetKeyword(PlayerRole.Guest,request.keyword);
            }
            catch(ArgumentException){}
        }

        public bool Submit(string topic,string rounds,out string error)
        {
            error="Host와 Guest가 연결된 상태에서 Host만 제출할 수 있습니다.";
            return (IsDevMode || CanReady) && LocalRole==PlayerRole.Host && (manager.IsHost || IsDevMode) && Session.Submit(PlayerRole.Host,topic,rounds,out error);
        }

        private void ReceiveReady(ulong sender,FastBufferReader reader)
        {
            if(!manager.IsHost || sender!=guestId || !PeerConnected)return;
            if(!TryRead(reader,out var json))return;
            try
            {
                var request=JsonUtility.FromJson<ReadyRequest>(json);
                if(request==null || request.sessionId!=Session.SessionId || request.sequence<=lastGuestSequence)return;
                lastGuestSequence=request.sequence;
                // Role is derived from the connection, never from a client-supplied role field.
                Session.SetReady(PlayerRole.Guest,request.ready);
            }
            catch(ArgumentException){ }
        }

        private void ReceiveSync(ulong sender,FastBufferReader reader)
        { if(manager.IsHost && sender==guestId && PeerConnected)SendState(sender); }

        private void ReceiveState(ulong sender,FastBufferReader reader)
        {
            if(manager.IsHost || sender!=NetworkManager.ServerClientId || stopping)return;
            if(!TryRead(reader,out var json))return;
            try
            {
                if(Session.ApplySnapshot(JsonUtility.FromJson<LobbySnapshot>(json),!receivedSnapshot))
                {
                    receivedSnapshot=true;Message="Host와 동기화되었습니다.";Changed?.Invoke();
                }
            }
            catch(ArgumentException){ }
        }

        private static bool TryRead(FastBufferReader reader,out string value)
        {
            value=null;
            if(reader.Length>MaxMessageBytes || reader.Length<4)return false;
            try { reader.ReadValueSafe(out value);return value!=null && value.Length<=6000; }
            catch(Exception e) when(e is OverflowException || e is ArgumentException){return false;}
        }

        private void SessionChanged()
        {
            if(manager && manager.IsHost && PeerConnected && guestId.HasValue)SendState(guestId.Value);
            Changed?.Invoke();
        }
        private void SendState(ulong id) => Send(StateMessage,id,JsonUtility.ToJson(Session.Snapshot()));
        private void Send(string name,ulong recipient,string json)
        {
            if(!manager || !manager.IsListening || manager.CustomMessagingManager==null)return;
            using(var writer=new FastBufferWriter(MaxMessageBytes,Allocator.Temp))
            {
                writer.WriteValueSafe(json);
                manager.CustomMessagingManager.SendNamedMessage(name,recipient,writer,NetworkDelivery.ReliableFragmentedSequenced);
            }
        }

        public void Leave(string reason="접속을 종료했습니다.")
        {
            stopping=true;PeerConnected=false;receivedSnapshot=false;guestId=null;
            IsDevMode=false;
            if (Session != null) Session.IsDevMode = false;
            Stage=ConnectionStage.Offline;Message=reason;
            if(manager && manager.IsListening)manager.Shutdown();
            Changed?.Invoke();
        }

        private void OnDestroy()
        {
            Session.Changed-=SessionChanged;
            if(!manager)return;
            stopping=true;
            manager.OnClientConnectedCallback-=OnConnected;
            manager.OnClientDisconnectCallback-=OnDisconnected;
            manager.OnTransportFailure-=OnTransportFailure;
            manager.ConnectionApprovalCallback=null;
            manager.Shutdown();Destroy(manager.gameObject);
        }
    }
}
