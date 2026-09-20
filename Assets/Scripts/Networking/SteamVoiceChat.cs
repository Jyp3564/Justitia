using System;
using Steamworks;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Justitia
{
    // No echo to the sender; STT receives local PCM separately from remote playback.
    [DefaultExecutionOrder(50)]
    public sealed class SteamVoiceChat : MonoBehaviour
    {
        private const string MessageName="justitia/voice/v1";
        private const int Rate=24000, Chunk=900, MaxCompressed=14400;
        public bool Muted { get; private set; }
        public float Volume { get; private set; }=1;
        public bool Recording => recording;
        public bool InputBlocked { get; set; }
        public bool SpeechAllowed { get; private set; }=true;
        public event Action CaptureStarted;
        public event Action<byte[],int> LocalPcm;
        public event Action<bool> CaptureEnded;
        private readonly byte[] localPcm=new byte[16000*2];
        private bool cancelled, limitReached;
        private float captureStartedAt, drainStartedAt;
        public void SetSpeakingAllowed(bool allowed)
        {SpeechAllowed=allowed;if(!allowed)StopCapture(true);}
        public string Status { get; private set; }="상대 연결 대기";
        private LanLobbyConnection network;
        private CustomMessagingManager registered;
        private AudioSource speaker;
        private AudioClip clip;
        private bool recording, draining;
        private float nextCapture, receiveStarted;
        private uint sequence, incomingSequence;
        private int parts, mask, assembledLength;
        private readonly byte[] compressed=new byte[MaxCompressed], pcm=new byte[Rate*2];
        private byte[] assembled=new byte[MaxCompressed];
        private readonly float[] samples=new float[Rate/2];
        private readonly object audioLock=new object();
        private int read,write,count;

        public void Initialize(LanLobbyConnection connection)
        {
            network=connection;
            Muted=PlayerPrefs.GetInt("VoiceMuted",0)!=0;
            Volume=Mathf.Clamp01(PlayerPrefs.GetFloat("VoiceVolume",1));
            speaker=gameObject.AddComponent<AudioSource>();speaker.playOnAwake=false;speaker.loop=true;
            speaker.spatialBlend=0;speaker.ignoreListenerPause=true;speaker.volume=Volume;
            clip=AudioClip.Create("Remote voice",Rate,1,Rate,true,ReadAudio);
            speaker.clip=clip;speaker.Play();
        }
        public void SetMuted(bool value)
        {
            Muted=value;PlayerPrefs.SetInt("VoiceMuted",value?1:0);
            if(value)StopCapture(true);
        }
        public void SetVolume(float value)
        {Volume=Mathf.Clamp01(value);if(speaker)speaker.volume=Volume;PlayerPrefs.SetFloat("VoiceVolume",Volume);}

        private void Update()
        {
            if(!network)return;
            var manager=network.Manager;
            bool connected=manager && manager.IsListening && network.PeerConnected;
            var messaging=connected?manager.CustomMessagingManager:null;
            if(registered!=messaging)
            {
                registered?.UnregisterNamedMessageHandler(MessageName);registered=messaging;
                registered?.RegisterNamedMessageHandler(MessageName,Receive);
                mask=parts=0;incomingSequence=0;sequence=0;
                lock(audioLock){read=write=count=0;}
            }
            bool inSession=network.Stage==ConnectionStage.Hosting || network.Stage==ConnectionStage.Connected;
            bool held=UnityEngine.InputSystem.Keyboard.current?.vKey.isPressed==true;
            if(!held)limitReached=false;
            bool allowed=inSession && SteamClientRuntime.Ready && !Muted && SpeechAllowed && !InputBlocked && Application.isFocused;
            if(!allowed || !held || limitReached)
            {
                StopCapture(!allowed);Drain();
                Status=!SteamClientRuntime.Ready?"음성: Steam 로그인 필요":!inSession?"음성: 세션 입장 필요":Muted?"마이크 꺼짐":!SpeechAllowed?"발언 차례 대기":limitReached?"30초 제한 · V 키를 놓아 주세요":"V 키를 누르는 동안 발언 · 놓으면 한국어 STT";
                return;
            }
            if(draining){Drain();return;}
            if(!recording){SteamUser.StartVoiceRecording();recording=true;cancelled=false;captureStartedAt=Time.realtimeSinceStartup;CaptureStarted?.Invoke();}
            Status="발언 중 · V 키를 놓으면 STT 출력";
            if(Time.realtimeSinceStartup-captureStartedAt>=30){limitReached=true;StopCapture(false);return;}
            if(Time.realtimeSinceStartup<nextCapture)return;
            nextCapture=Time.realtimeSinceStartup+0.05f;
            var result=SteamUser.GetVoice(true,compressed,(uint)compressed.Length,out uint bytes);
            if(result==EVoiceResult.k_EVoiceResultNoData)return;
            if(result!=EVoiceResult.k_EVoiceResultOK){Status="마이크 입력 확인 필요: "+result;return;}
            if(bytes==0)return;
            DecodeLocal(bytes);
            if(!connected)return;
            sequence++;int total=((int)bytes+Chunk-1)/Chunk;
            System.Collections.Generic.IEnumerable<ulong> peers=manager.IsServer?manager.ConnectedClientsIds:new ulong[]{NetworkManager.ServerClientId};
            foreach(ulong peer in peers)
            {
                if(!network.IsVoicePeer(peer))continue;
                for(int index=0;index<total;index++)
                {
                    int length=Math.Min(Chunk,(int)bytes-index*Chunk);
                    using(var writer=new FastBufferWriter(Chunk+16,Allocator.Temp))
                    {
                        writer.WriteValueSafe(sequence);writer.WriteValueSafe((byte)index);writer.WriteValueSafe((byte)total);
                        writer.WriteValueSafe((ushort)length);writer.WriteBytesSafe(compressed,length,index*Chunk);
                        registered.SendNamedMessage(MessageName,peer,writer,NetworkDelivery.Unreliable);
                    }
                }
            }
        }
        private void Receive(ulong sender,FastBufferReader reader)
        {
            if(!network.IsVoicePeer(sender) || !SteamClientRuntime.Ready || reader.Length<8)return;
            reader.ReadValueSafe(out uint seq);reader.ReadValueSafe(out byte index);reader.ReadValueSafe(out byte total);reader.ReadValueSafe(out ushort length);
            if(total==0 || total>16 || index>=total || length==0 || length>Chunk || reader.Length!=8+length || (index<total-1 && length!=Chunk))return;
            if(seq!=incomingSequence)
            {
                if(incomingSequence!=0 && unchecked((int)(seq-incomingSequence))<=0)return;
                incomingSequence=seq;mask=0;parts=total;assembledLength=0;receiveStarted=Time.realtimeSinceStartup;
            }
            if(parts!=total || Time.realtimeSinceStartup-receiveStarted>0.3f || (mask&(1<<index))!=0)return;
            reader.ReadBytesSafe(ref assembled,length,index*Chunk);
            mask|=1<<index;assembledLength+=length;
            if(mask!=(1<<parts)-1)return;
            if(SteamUser.DecompressVoice(assembled,(uint)assembledLength,pcm,(uint)pcm.Length,out uint bytes,Rate)!=EVoiceResult.k_EVoiceResultOK)return;
            lock(audioLock)
            {
                int incoming=(int)bytes/2;
                if(incoming>samples.Length)return;
                // Drop stale audio rather than accumulating conversational delay.
                if(count+incoming>samples.Length){read=write=count=0;}
                for(int i=0;i<incoming;i++){samples[write]=(short)(pcm[i*2]|pcm[i*2+1]<<8)/32768f;write=(write+1)%samples.Length;count++;}
            }
        }
        private void ReadAudio(float[] output)
        {
            lock(audioLock)
                for(int i=0;i<output.Length;i++)
                {if(count==0){output[i]=0;continue;}output[i]=samples[read];read=(read+1)%samples.Length;count--;}
        }
        private void DecodeLocal(uint bytes)
        {
            if(cancelled || LocalPcm==null)return;
            if(SteamUser.DecompressVoice(compressed,bytes,localPcm,(uint)localPcm.Length,out uint decoded,16000)==EVoiceResult.k_EVoiceResultOK)
                LocalPcm.Invoke(localPcm,(int)decoded);
        }
        private void StopCapture(bool cancel)
        {
            if(cancel && (recording || draining))cancelled=true;
            if(!recording)return;
            if(SteamClientRuntime.Ready)SteamUser.StopVoiceRecording();
            recording=false;draining=true;drainStartedAt=Time.realtimeSinceStartup;
        }
        private void Drain()
        {
            if(!draining)return;
            var result=EVoiceResult.k_EVoiceResultNotRecording;
            if(SteamClientRuntime.Ready)
            {
                result=SteamUser.GetVoice(true,compressed,(uint)compressed.Length,out uint bytes);
                if(result==EVoiceResult.k_EVoiceResultOK && bytes>0)DecodeLocal(bytes);
            }
            if(result==EVoiceResult.k_EVoiceResultNotRecording || Time.realtimeSinceStartup-drainStartedAt>2)
            {draining=false;CaptureEnded?.Invoke(cancelled);}
        }
        private void OnDisable(){StopCapture(true);lock(audioLock){count=read=write=0;}}
        private void OnDestroy()
        {StopCapture(true);registered?.UnregisterNamedMessageHandler(MessageName);if(speaker)speaker.Stop();if(clip)Destroy(clip);}
    }
}
