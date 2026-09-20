using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace Justitia
{
    public sealed class SteamLobbyRoom : MonoBehaviour
    {
        private const string GameKey="justitia.court.v1";
        public LanLobbyConnection Network { get; private set; }
        public ulong LobbyId=>lobby.m_SteamID;
        public bool Busy { get; private set; }
        public bool Ready=>SteamClientRuntime.Ready;
        public bool CanInvite=>Ready && LobbyId!=0 && Network.LocalRole==PlayerRole.Host && !Network.PeerConnected;
        public string Status { get; private set; }
        public event Action Changed;
        private SteamSocketsTransport transport;
        private CSteamID lobby,host;
        private int generation;
        private float deadline;
        private bool closing;
        private readonly List<IDisposable> calls=new List<IDisposable>();
        private Callback<GameLobbyJoinRequested_t> invite;
        private Callback<LobbyChatUpdate_t> members;
        private Callback<LobbyDataUpdate_t> metadata;
        public sealed class FriendRoom { public ulong Id; public string Name; }
        public readonly List<FriendRoom> FriendRooms=new List<FriendRoom>();
        private readonly Dictionary<ulong,string> roomCandidates=new Dictionary<ulong,string>();
        public void RefreshFriendRooms()
        {
            FriendRooms.Clear();roomCandidates.Clear();
            if(!Ready){Changed?.Invoke();return;}
            int count=SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for(int i=0;i<count;i++)
            {
                var friend=SteamFriends.GetFriendByIndex(i,EFriendFlags.k_EFriendFlagImmediate);
                if(!SteamFriends.GetFriendGamePlayed(friend,out var game) || game.m_gameID.AppID().m_AppId!=SteamClientRuntime.AppId || !game.m_steamIDLobby.IsLobby())continue;
                roomCandidates[game.m_steamIDLobby.m_SteamID]=SteamFriends.GetFriendPersonaName(friend);
                SteamMatchmaking.RequestLobbyData(game.m_steamIDLobby);
            }
            Changed?.Invoke();
        }
        private void ReadFriendRoom(ulong id)
        {
            if(!roomCandidates.TryGetValue(id,out var name))return;
            var room=new CSteamID(id);
            if(SteamMatchmaking.GetLobbyData(room,"game")!=GameKey || SteamMatchmaking.GetLobbyData(room,"protocol")!="2")return;
            if(SteamMatchmaking.GetNumLobbyMembers(room)>=2)return;
            FriendRooms.RemoveAll(item=>item.Id==id);FriendRooms.Add(new FriendRoom{Id=id,Name=name});Changed?.Invoke();
        }

        public void Initialize(LanLobbyConnection network)
        {
            Network=network;transport=gameObject.AddComponent<SteamSocketsTransport>();
            transport.AllowPeer=IsAllowedPeer;
            var runtime=SteamClientRuntime.Ensure();Status=runtime.Status;
            Network.Changed+=OnNetworkChanged;
            if(!Ready)return;
            invite=Callback<GameLobbyJoinRequested_t>.Create(data=>Join(data.m_steamIDLobby.m_SteamID));
            members=Callback<LobbyChatUpdate_t>.Create(data=>{if(data.m_ulSteamIDLobby==LobbyId)CheckOwner();});
            metadata=Callback<LobbyDataUpdate_t>.Create(data=>{if(data.m_ulSteamIDLobby==LobbyId)CheckOwner();else if(data.m_bSuccess!=0)ReadFriendRoom(data.m_ulSteamIDLobby);});
            var args=Environment.GetCommandLineArgs();int arg=Array.IndexOf(args,"+connect_lobby");
            if(arg>=0 && arg+1<args.Length && ulong.TryParse(args[arg+1],out var id))Join(id);
        }
        private bool BeginOperation()
        {
            if(!Ready){Status=SteamClientRuntime.Instance?SteamClientRuntime.Instance.Status:"Steam을 실행해 주세요.";Changed?.Invoke();return false;}
            if(Busy || !Network.Available || LobbyId!=0){Status="현재 연결을 종료한 뒤 새 방에 참가해 주세요.";Changed?.Invoke();return false;}
            Busy=true;generation++;deadline=Time.realtimeSinceStartup+25;Status="Steam 방에 연결 중…";Changed?.Invoke();return true;
        }
        public void Create()
        {
            if(!BeginOperation())return;int operation=generation;
            CallResult<LobbyCreated_t> result=null;
            result=CallResult<LobbyCreated_t>.Create((data,failed)=>
            {
                calls.Remove(result);result.Dispose();
                if(operation!=generation){if(!failed && data.m_eResult==EResult.k_EResultOK)SteamMatchmaking.LeaveLobby(new CSteamID(data.m_ulSteamIDLobby));return;}
                Busy=false;
                if(failed || data.m_eResult!=EResult.k_EResultOK){Fail("Steam 방을 만들지 못했습니다. 잠시 후 다시 시도해 주세요.");return;}
                lobby=new CSteamID(data.m_ulSteamIDLobby);host=SteamUser.GetSteamID();
                bool valid=SteamMatchmaking.SetLobbyData(lobby,"game",GameKey) &&
                    SteamMatchmaking.SetLobbyData(lobby,"protocol","2") &&
                    SteamMatchmaking.SetLobbyData(lobby,"host",host.m_SteamID.ToString());
                SteamMatchmaking.SetLobbyJoinable(lobby,true);
                if(!valid || !Network.StartSteam(true,transport)){Fail("Steam 방의 게임 연결을 시작하지 못했습니다.");return;}
                Status="Steam 방을 만들었습니다. 친구를 초대해 주세요.";Changed?.Invoke();
            });
            calls.Add(result);result.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly,2));
        }
        public void Join(ulong id)
        {
            if(!new CSteamID(id).IsLobby()){Status="올바른 Steam 방 번호를 입력해 주세요.";Changed?.Invoke();return;}
            if(!BeginOperation())return;int operation=generation;
            CallResult<LobbyEnter_t> result=null;
            result=CallResult<LobbyEnter_t>.Create((data,failed)=>
            {
                calls.Remove(result);result.Dispose();
                bool success=!failed && data.m_EChatRoomEnterResponse==(uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess;
                if(operation!=generation){if(success)SteamMatchmaking.LeaveLobby(new CSteamID(data.m_ulSteamIDLobby));return;}
                Busy=false;
                if(!success){Fail("Steam 방에 참가하지 못했습니다. 초대와 방 인원을 확인해 주세요.");return;}
                lobby=new CSteamID(data.m_ulSteamIDLobby);host=SteamMatchmaking.GetLobbyOwner(lobby);
                if(SteamMatchmaking.GetLobbyData(lobby,"game")!=GameKey || SteamMatchmaking.GetLobbyData(lobby,"protocol")!="2" ||
                    SteamMatchmaking.GetLobbyData(lobby,"host")!=host.m_SteamID.ToString() || host==SteamUser.GetSteamID())
                {Fail("호환되는 다른 Host의 방이 아닙니다.");return;}
                transport.TargetSteamId=host.m_SteamID;
                if(!Network.StartSteam(false,transport)){Fail("Steam Host에 연결하지 못했습니다.");return;}
                Status="Steam 방에 참가했습니다.";Changed?.Invoke();
            });
            calls.Add(result);result.Set(SteamMatchmaking.JoinLobby(new CSteamID(id)));
        }
        private bool IsAllowedPeer(ulong peer)
        {
            if(!Ready || LobbyId==0 || SteamMatchmaking.GetLobbyOwner(lobby)!=SteamUser.GetSteamID() || Network.Session.Phase!=LobbyPhase.Preparing)return false;
            for(int i=0;i<SteamMatchmaking.GetNumLobbyMembers(lobby);i++)
                if(SteamMatchmaking.GetLobbyMemberByIndex(lobby,i).m_SteamID==peer && peer!=SteamUser.GetSteamID().m_SteamID)return true;
            return false;
        }
        public void InviteFriends()
        {
            if(!CanInvite)return;
            if(!SteamUtils.IsOverlayEnabled()){Status="Steam 오버레이가 비활성화되어 있습니다. 아래 친구 목록에서 초대하세요.";Changed?.Invoke();return;}
            SteamFriends.ActivateGameOverlayInviteDialog(lobby);
            Status="Steam 초대 창을 요청했습니다. 창이 나타나지 않으면 아래 친구 목록을 이용하세요.";Changed?.Invoke();
        }
        public void InviteFriend(ulong friendId)
        {
            if(!CanInvite)return;
            Status=SteamMatchmaking.InviteUserToLobby(lobby,new CSteamID(friendId))
                ? "초대를 보냈습니다. 친구가 Steam에서 수락하면 자동 입장합니다."
                : "초대를 보내지 못했습니다. 잠시 후 다시 시도해 주세요.";
            Changed?.Invoke();
        }
        public void CopyRoomId(){if(LobbyId!=0)GUIUtility.systemCopyBuffer=LobbyId.ToString();}
        public void Leave()
        {
            if(closing)return;closing=true;generation++;Busy=false;
            Network.Leave();CloseLobby();closing=false;Status="Steam 방에서 나왔습니다.";Changed?.Invoke();
        }
        private void OnNetworkChanged()
        {
            if(closing || LobbyId==0)return;
            if(Network.Stage==ConnectionStage.Offline){CloseLobby();Status=Network.Message;}
            else if(Network.LocalRole==PlayerRole.Host)
                SteamMatchmaking.SetLobbyJoinable(lobby,!Network.PeerConnected && Network.Session.Phase==LobbyPhase.Preparing);
            Changed?.Invoke();
        }
        private void CheckOwner()
        {
            if(LobbyId!=0 && SteamMatchmaking.GetLobbyOwner(lobby)!=host)
            {Network.Leave("Steam Host가 떠나 게임을 중단했습니다.");return;}
            // Steam may report a lobby departure before the socket closes.
            if(Network.PeerConnected && SteamMatchmaking.GetNumLobbyMembers(lobby)<2)
                Network.Leave("상대가 Steam 방을 떠나 게임을 중단했습니다.");
        }
        private void CloseLobby()
        {if(Ready && LobbyId!=0)SteamMatchmaking.LeaveLobby(lobby);lobby=CSteamID.Nil;host=CSteamID.Nil;}
        private void Fail(string message){CloseLobby();Status=message;Changed?.Invoke();}
        private void Update()
        {if(Busy && Time.realtimeSinceStartup>deadline){generation++;Busy=false;Status="Steam 응답 시간이 초과되었습니다. 다시 시도해 주세요.";Changed?.Invoke();}}
        private void OnDestroy()
        {
            generation++;if(Network){Network.Changed-=OnNetworkChanged;if(LobbyId!=0)Network.Leave();}
            transport?.Shutdown();CloseLobby();
            invite?.Dispose();members?.Dispose();metadata?.Dispose();
            foreach(var call in calls)call.Dispose();calls.Clear();
        }
    }
}
