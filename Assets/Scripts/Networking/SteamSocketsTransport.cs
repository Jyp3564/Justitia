using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using Unity.Netcode;
using UnityEngine;

namespace Justitia
{
    // Steam authenticates peer identities. Only members of the current game lobby may connect.
    public sealed class SteamSocketsTransport : NetworkTransport
    {
        public ulong TargetSteamId;
        public Func<ulong,bool> AllowPeer;
        public override ulong ServerClientId=>0;
        private const int Port=0, MaxPacket=65536;
        private readonly Dictionary<ulong,HSteamNetConnection> connections=new Dictionary<ulong,HSteamNetConnection>();
        private readonly Queue<(NetworkEvent kind,ulong peer,byte[] data)> events=new Queue<(NetworkEvent,ulong,byte[])>();
        private readonly IntPtr[] incoming=new IntPtr[1];
        private HSteamListenSocket listener;
        private Callback<SteamNetConnectionStatusChangedCallback_t> statusCallback;
        private bool server,running;

        public override void Initialize(NetworkManager networkManager=null)
        {statusCallback?.Dispose();statusCallback=Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatus);}
        public override bool StartServer()
        {
            if(!SteamClientRuntime.Ready)return false;
            server=true;
            listener=SteamNetworkingSockets.CreateListenSocketP2P(Port,0,null);
            return running=listener!=HSteamListenSocket.Invalid;
        }
        public override bool StartClient()
        {
            if(!SteamClientRuntime.Ready || TargetSteamId==0)return false;
            server=false;
            var identity=new SteamNetworkingIdentity();identity.SetSteamID(new CSteamID(TargetSteamId));
            var socket=SteamNetworkingSockets.ConnectP2P(ref identity,Port,0,null);
            if(socket==HSteamNetConnection.Invalid)return false;
            connections[0]=socket;return running=true;
        }
        private void OnStatus(SteamNetConnectionStatusChangedCallback_t change)
        {
            if(!running)return;
            ulong peer=server?change.m_info.m_identityRemote.GetSteamID().m_SteamID:0;
            if(server && change.m_info.m_hListenSocket!=listener)return;
            if(!server && (!connections.TryGetValue(0,out var target) || target!=change.m_hConn))return;
            if(change.m_info.m_eState==ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting && server)
            {
                if(peer==0 || connections.Count>=1 || AllowPeer==null || !AllowPeer(peer))
                {SteamNetworkingSockets.CloseConnection(change.m_hConn,1000,"Lobby admission denied",false);return;}
                if(SteamNetworkingSockets.AcceptConnection(change.m_hConn)!=EResult.k_EResultOK)
                {SteamNetworkingSockets.CloseConnection(change.m_hConn,1000,"Accept failed",false);return;}
                connections[peer]=change.m_hConn;
            }
            if(!connections.TryGetValue(peer,out var known) || known!=change.m_hConn)return;
            if(change.m_info.m_eState==ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
                events.Enqueue((NetworkEvent.Connect,peer,null));
            else if(change.m_info.m_eState==ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer ||
                    change.m_info.m_eState==ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
            {
                SteamNetworkingSockets.CloseConnection(change.m_hConn,0,"Closed",false);
                connections.Remove(peer);events.Enqueue((NetworkEvent.Disconnect,peer,null));
            }
        }
        protected override void OnEarlyUpdate()
        {
            if(!running || !SteamClientRuntime.Ready)return;
            foreach(var pair in new List<KeyValuePair<ulong,HSteamNetConnection>>(connections))
            {
                for(int i=0;i<32 && events.Count<128;i++)
                {
                    int received=SteamNetworkingSockets.ReceiveMessagesOnConnection(pair.Value,incoming,1);
                    if(received<=0)break;
                    try
                    {
                        var message=Marshal.PtrToStructure<SteamNetworkingMessage_t>(incoming[0]);
                        if(message.m_cbSize<0 || message.m_cbSize>MaxPacket)
                        {Close(pair.Key);events.Enqueue((NetworkEvent.Disconnect,pair.Key,null));break;}
                        var data=new byte[message.m_cbSize];Marshal.Copy(message.m_pData,data,0,data.Length);
                        events.Enqueue((NetworkEvent.Data,pair.Key,data));
                    }
                    finally{SteamNetworkingMessage_t.Release(incoming[0]);incoming[0]=IntPtr.Zero;}
                }
            }
        }
        public override NetworkEvent PollEvent(out ulong clientId,out ArraySegment<byte> payload,out float receiveTime)
        {
            clientId=0;payload=default;receiveTime=Time.realtimeSinceStartup;
            if(events.Count==0)return NetworkEvent.Nothing;
            var item=events.Dequeue();clientId=item.peer;
            if(item.data!=null)payload=new ArraySegment<byte>(item.data);
            return item.kind;
        }
        public override void Send(ulong clientId,ArraySegment<byte> payload,NetworkDelivery delivery)
        {
            if(!running || !connections.TryGetValue(clientId,out var socket))return;
            if(payload.Count>MaxPacket){Close(clientId);events.Enqueue((NetworkEvent.Disconnect,clientId,null));return;}
            var handle=GCHandle.Alloc(payload.Array,GCHandleType.Pinned);
            try
            {
                int flags=delivery==NetworkDelivery.Unreliable || delivery==NetworkDelivery.UnreliableSequenced
                    ? Constants.k_nSteamNetworkingSend_UnreliableNoDelay : Constants.k_nSteamNetworkingSend_Reliable;
                var result=SteamNetworkingSockets.SendMessageToConnection(socket,IntPtr.Add(handle.AddrOfPinnedObject(),payload.Offset),
                    (uint)payload.Count,flags,out _);
                if(flags==Constants.k_nSteamNetworkingSend_UnreliableNoDelay)return;
                if(result!=EResult.k_EResultOK){Close(clientId);events.Enqueue((NetworkEvent.Disconnect,clientId,null));}
            }
            finally{handle.Free();}
        }
        private void Close(ulong peer)
        {
            if(connections.TryGetValue(peer,out var socket) && SteamClientRuntime.Ready)
                SteamNetworkingSockets.CloseConnection(socket,0,"Session closed",false);
            connections.Remove(peer);
        }
        public override void DisconnectRemoteClient(ulong clientId)=>Close(clientId);
        public override void DisconnectLocalClient()=>Close(0);
        public override ulong GetCurrentRtt(ulong clientId)
        {
            if(!SteamClientRuntime.Ready || !connections.TryGetValue(clientId,out var socket))return 0;
            var info=new SteamNetConnectionRealTimeStatus_t();var lane=new SteamNetConnectionRealTimeLaneStatus_t();
            return SteamNetworkingSockets.GetConnectionRealTimeStatus(socket,ref info,0,ref lane)==EResult.k_EResultOK?(ulong)Math.Max(0,info.m_nPing):0;
        }
        public override void Shutdown()
        {
            running=false;
            foreach(var peer in new List<ulong>(connections.Keys))Close(peer);
            if(SteamClientRuntime.Ready && listener!=HSteamListenSocket.Invalid)SteamNetworkingSockets.CloseListenSocket(listener);
            listener=HSteamListenSocket.Invalid;events.Clear();statusCallback?.Dispose();statusCallback=null;
        }
        private void OnDestroy()=>Shutdown();
    }
}
