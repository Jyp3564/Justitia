using System;
using Steamworks;
using UnityEngine;

namespace Justitia
{
    [DefaultExecutionOrder(-1000)]
    public sealed class SteamClientRuntime : MonoBehaviour
    {
        [Serializable] public sealed class Config { public uint appId; }
        public static SteamClientRuntime Instance { get; private set; }
        public static bool Ready => Instance && Instance.initialized;
        public string Status { get; private set; }
        private bool initialized;
        public bool OverlayActive { get; private set; }
        private Callback<GameOverlayActivated_t> overlay;
        public static uint AppId => JsonUtility.FromJson<Config>(Resources.Load<TextAsset>("SteamConfig").text).appId;

        public static SteamClientRuntime Ensure()
        {
            if(!Instance)new GameObject("Steam Client").AddComponent<SteamClientRuntime>();
            return Instance;
        }
        private void Awake()
        {
            if(Instance){Destroy(gameObject);return;}
            Instance=this;DontDestroyOnLoad(gameObject);
            try
            {
                if(SteamAPI.InitEx(out _)!=ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
                {Status="Steam에 로그인한 뒤 게임을 다시 실행해 주세요.";return;}
                initialized=true;
                if(SteamUtils.GetAppID().m_AppId!=AppId)
                {SteamAPI.Shutdown();initialized=false;Status="Steam App ID 설정이 실행 환경과 다릅니다.";return;}
                SteamNetworkingUtils.InitRelayNetworkAccess();
                overlay=Callback<GameOverlayActivated_t>.Create(data=>OverlayActive=data.m_bActive!=0);
                Status="Steam 연결됨";
            }
            catch(DllNotFoundException){Status="Steam 라이브러리를 불러오지 못했습니다. 게임 폴더 전체를 확인해 주세요.";}
            catch(Exception){Status="Steam 초기화에 실패했습니다. Steam과 게임을 다시 실행해 주세요.";}
        }
        private void Update(){if(initialized)SteamAPI.RunCallbacks();}
        private void OnDestroy()
        {
            if(Instance!=this)return;
            overlay?.Dispose();
            if(initialized)SteamAPI.Shutdown();
            initialized=false;Instance=null;
        }
    }
}
