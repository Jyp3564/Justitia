using UnityEngine;
using TMPro;

namespace Justitia
{
    public sealed class LobbyPanel : MonoBehaviour
    {
        public MouseSeatView SeatView;
        public GameObject PreviewControls;
        public TMP_FontAsset FallbackFont;
        public PlayerRole LocalRole => Network.LocalRole;
        public LanLobbyConnection Network { get; private set; }
        public SteamLobbyRoom SteamRoom { get; private set; }
        public SteamVoiceChat Voice { get; private set; }
        public LocalSpeechToText Speech { get; private set; }
        public JusticeCaseAI GoddessAI { get; private set; }
        private UnityEngine.UI.Button keywordButton,startCaseButton;
        private GameObject roundSettings,caseHud;
        private TMP_Text goddessText;
        private TMP_Text transcriptLabel;
        private GameObject voicePanel;
        private TMP_Text voiceStatus,muteLabel,volumeLabel;
        private TMP_Text voiceHint;
        public LobbySession Session => Network.Session;
        public TMP_InputField TopicInput { get; private set; }
        public TMP_InputField RoundInput { get; private set; }
        public UnityEngine.UI.Button ReadyButton { get; private set; }
        public UnityEngine.UI.Button SubmitButton { get; private set; }
        private TMP_FontAsset font;
        private TMP_Text heading, status, readyText, errorText, summary, counter;
        private GameObject preparation, setup, waiting, submitted, connection, connectionToolbar;
        private TMP_InputField addressInput;
        private TMP_Text connectionStatus;
        private bool previousPreviewActive;
        private bool steamMode=true;
        private GameObject steamConnect,lanConnect;
        private UnityEngine.UI.Button steamHost,lanHost,lanJoin,steamTab,lanTab,inviteButton;
        private GameObject backdrop, worldHint, friendList;
        private bool menuOpen, inWorld;
        private int viewedRole=-2;
        private LobbyPhase previousPhase;
        private enum FrontPage { Main, Join, Settings }
        private FrontPage frontPage;
        private bool wasOnline;
        private GameObject mainMenu,frontBack;
        private TMP_Text mainStatus,mapStatus,lobbyVoice;
        private UnityEngine.UI.Button createRoomButton,resumeButton,mapButton;
        private Transform roomListContent;
        private RectTransform mainFrame,cardRect;

        public void ShowMainMenu(){frontPage=FrontPage.Main;voicePanel.SetActive(false);Refresh();}
        public void ShowJoinMenu(){frontPage=FrontPage.Join;Refresh();SteamRoom.RefreshFriendRooms();}
        public void ShowSettings(){frontPage=FrontPage.Settings;friendList.SetActive(false);voicePanel.SetActive(true);Refresh();}
        public void CreateRoom(){if(steamMode)SteamRoom.Create();else Network.Host();}
        private void QuitGame()
        {
            SteamRoom.Leave();PlayerPrefs.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit();
#endif
        }

        private void RefreshRooms()
        {
            if(!roomListContent)return;
            foreach(Transform child in roomListContent){child.gameObject.SetActive(false);Destroy(child.gameObject);}
            if(SteamRoom.FriendRooms.Count==0)Text(roomListContent,"열린 친구 방이 없습니다.\n친구가 방을 만든 뒤 새로고침하거나 초대를 수락하세요.",16,66,new Color(0.7f,0.79f,0.88f));
            foreach(var room in SteamRoom.FriendRooms)
            {
                ulong id=room.Id;
                var button=Button(roomListContent,room.Name+"의 방 · 참가",()=>SteamRoom.Join(id));
                button.interactable=!SteamRoom.Busy && Network.Available;
            }
        }

        private void Update()
        {
            if(!Network)return;
            if(startCaseButton)startCaseButton.interactable=Network.CanReady && LocalRole==PlayerRole.Host && int.TryParse(RoundInput.text,out var roundCount) && roundCount>=1 && roundCount<=5;
            var canvasSize=((RectTransform)transform).rect.size;
            if(mainFrame)mainFrame.localScale=Vector3.one*Mathf.Min(canvasSize.x/1280f,canvasSize.y/720f);
            if(cardRect)cardRect.localScale=Vector3.one*Mathf.Min(1,Mathf.Min((canvasSize.x-24)/820f,(canvasSize.y-24)/680f));
            if(Voice && voiceStatus){voiceStatus.text=Voice.Status;muteLabel.text=Voice.Muted?"마이크 켜기":"마이크 끄기";volumeLabel.text=$"상대 음성 볼륨: {Voice.Volume:P0}";}
            if(voiceHint && Voice)voiceHint.text="마우스로 둘러보기 · Esc: 설정 / 준비\n"+Voice.Status;
            bool overlay=SteamClientRuntime.Instance && SteamClientRuntime.Instance.OverlayActive;
            bool inLobby=wasOnline && !inWorld && Network.Stage!=ConnectionStage.Connecting;
            Voice.InputBlocked=(!inWorld && !inLobby) || (inWorld && menuOpen) || voicePanel.activeSelf || friendList.activeSelf || Session.Phase==LobbyPhase.HostSetup || overlay;
            if(lobbyVoice)lobbyVoice.text=Voice.Status+"\n"+Speech.Status+(string.IsNullOrEmpty(Speech.LastTranscript)?"":" · "+Speech.LastTranscript);
            if(transcriptLabel && Speech)transcriptLabel.text=Speech.Status+"\n"+Speech.LastTranscript;
            if(caseHud){caseHud.SetActive(inWorld && !menuOpen && Session.CommonQuestion.Length>0);goddessText.text="정의의 여신\n"+Session.CaseSummary+"\n\n"+Session.CommonQuestion;}
            if(inWorld && !overlay && UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame==true)
                SetMenuOpen(!menuOpen);
            else if(!inWorld && !overlay && UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame==true)
            {if(!wasOnline)ShowMainMenu();else{voicePanel.SetActive(false);friendList.SetActive(false);Refresh();}}
            if(SeatView)SeatView.UiInputActive=!inWorld || menuOpen || overlay;
        }

        public void SetMenuOpen(bool value)
        {
            menuOpen=value;
            if(!value){friendList.SetActive(false);voicePanel.SetActive(false);}
            Refresh();
        }

        private void OpenInvites()
        {
            var content=friendList.transform.Find("Content");
            foreach(Transform child in content){child.gameObject.SetActive(false);Destroy(child.gameObject);}
            friendList.SetActive(true);
            voicePanel.SetActive(false);
            Button(content,"친구 목록 닫기",()=>{friendList.SetActive(false);Refresh();});
            int count=Steamworks.SteamFriends.GetFriendCount(Steamworks.EFriendFlags.k_EFriendFlagImmediate);
            for(int i=0;i<count;i++)
            {
                var id=Steamworks.SteamFriends.GetFriendByIndex(i,Steamworks.EFriendFlags.k_EFriendFlagImmediate);
                var button=Button(content,Steamworks.SteamFriends.GetFriendPersonaName(id)+" · 초대",()=>SteamRoom.InviteFriend(id.m_SteamID));
                button.interactable=SteamRoom.CanInvite;
            }
            if(count==0)Text(content,"Steam 친구 목록이 비어 있습니다.",18,48,Color.white);
            SteamRoom.InviteFriends();
        }

        private void Start()
        {
            if(LocalSpeechToText.SelfTestRequested)return;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"--network-smoke")>=0) return;
            // Use the installed Windows Korean font; do not bundle an OS font in the project.
            font = TMP_FontAsset.CreateFontAsset("Malgun Gothic", "Regular", 48);
            if (!font) font = FallbackFont;
            Network = gameObject.AddComponent<LanLobbyConnection>();
            SteamRoom = gameObject.AddComponent<SteamLobbyRoom>();
            Voice=gameObject.AddComponent<SteamVoiceChat>();Voice.Initialize(Network);
            Speech=gameObject.AddComponent<LocalSpeechToText>();Speech.Initialize(Voice);
            GoddessAI=gameObject.AddComponent<JusticeCaseAI>();GoddessAI.Initialize(Network);
            BuildUI();
            Network.Changed += Refresh;
            SteamRoom.Changed += Refresh;
            SteamRoom.Initialize(Network);
            if (PreviewControls) { previousPreviewActive = PreviewControls.activeSelf; PreviewControls.SetActive(false); }
            Refresh();
        }

        private void ApplyRole()
        {
            if (SeatView)
            {
                SeatView.UiInputActive = !inWorld || menuOpen;
                int role=inWorld?(int)LocalRole:-1;
                if(role==viewedRole)return;
                viewedRole=role;
                if (!inWorld) SeatView.ShowOverview();
                else if (LocalRole == PlayerRole.Host) SeatView.ShowHost(); else SeatView.ShowGuest();
            }
        }

        public void ToggleReady()
        {
            bool ready = LocalRole == PlayerRole.Host ? Session.HostReady : Session.GuestReady;
            Network.SetReady(!ready);
        }

        public void SubmitSettings()
        {
            if(Session.CaseSummary.Length==0)return;
            if (!Network.Submit(Session.CaseSummary, RoundInput.text, out var error)) errorText.text = error;
        }

        private void Refresh()
        {
            if(Session==null || !heading)return;
            bool online=Network.Stage!=ConnectionStage.Offline;
            bool playing=online && Session.Phase==LobbyPhase.Submitted;
            bool phaseChanged=Session.Phase!=previousPhase;
            if(online && !wasOnline){voicePanel.SetActive(false);friendList.SetActive(false);}
            if(!online && wasOnline){Speech.Cancel();frontPage=FrontPage.Main;voicePanel.SetActive(false);friendList.SetActive(false);}
            if(phaseChanged){voicePanel.SetActive(false);friendList.SetActive(false);}
            if(playing && !inWorld)menuOpen=false;
            if(!playing)menuOpen=true;
            inWorld=playing;wasOnline=online;previousPhase=Session.Phase;
            if((online && Network.IsSteam) || SteamRoom.Busy)steamMode=true;
            bool front=!online && frontPage==FrontPage.Main;
            mainMenu.SetActive(front);
            backdrop.SetActive(!front && (!inWorld || menuOpen));
            worldHint.SetActive(inWorld && !menuOpen);
            if(SeatView && SeatView.StatusLabel)SeatView.StatusLabel.gameObject.SetActive(inWorld && !menuOpen);
            mainStatus.text=(steamMode?SteamRoom.Status:Network.Message)+"\n"+(steamMode?"Steam":"LAN");
            createRoomButton.interactable=!SteamRoom.Busy && Network.Available && (!steamMode || SteamRoom.Ready);
            bool preparing=online && Session.Phase==LobbyPhase.Preparing;
            bool configuring=online && Session.Phase==LobbyPhase.HostSetup;
            bool host=LocalRole==PlayerRole.Host;
            bool settings=!online && frontPage==FrontPage.Settings;
            if(settings)voicePanel.SetActive(true);
            connection.SetActive(!online && frontPage==FrontPage.Join);
            connectionToolbar.SetActive(online);
            frontBack.SetActive(!online && frontPage==FrontPage.Join);
            resumeButton.gameObject.SetActive(inWorld);
            connectionStatus.text=steamMode?SteamRoom.Status+(online?"\n"+Network.Message:""):Network.Message;
            steamConnect.SetActive(steamMode);lanConnect.SetActive(!steamMode);
            steamTab.interactable=!SteamRoom.Busy && !steamMode;lanTab.interactable=!SteamRoom.Busy && steamMode;
            steamHost.interactable=SteamRoom.Ready && !SteamRoom.Busy && Network.Available;
            lanHost.interactable=lanJoin.interactable=!SteamRoom.Busy && Network.Available;
            inviteButton.gameObject.SetActive(online && Network.IsSteam && host);
            inviteButton.interactable=SteamRoom.CanInvite;
            if(!SteamRoom.CanInvite)friendList.SetActive(false);
            bool detail=!friendList.activeSelf && !voicePanel.activeSelf;
            preparation.SetActive(preparing && detail);
            bool generated=Session.CaseSummary.Length>0;
            setup.SetActive(configuring && !generated && detail);
            waiting.SetActive(false);
            submitted.SetActive((playing || (configuring && generated)) && detail);
            roundSettings.SetActive(configuring && generated && host);
            startCaseButton.gameObject.SetActive(configuring && generated && host);
            lobbyVoice.gameObject.SetActive(online && !voicePanel.activeSelf);
            heading.text=!online?(settings?"설정":"방 참가"):preparing?"대기 로비":configuring?(generated?"여신이 제시한 사건":"여신에게 사건 요청"):"게임 설정";
            status.text=!online?"친구와 함께 정의의 법정으로":$"Host {(Session.HostReady?"준비 완료":"대기")}   ·   Guest {(Network.PeerConnected?(Session.GuestReady?"준비 완료":"대기"):"연결 대기")}   |   내 역할: {LocalRole}";
            mapStatus.text=Session.MapId=="court"?"선택한 맵  ·  정의의 법정":"맵 선택  ·  Host가 선택해 주세요";
            mapButton.gameObject.SetActive(host);mapButton.interactable=preparing && Session.MapId!="court";
            mapButton.GetComponentInChildren<TMP_Text>().text=Session.MapId=="court"?"정의의 법정 · 선택됨":"정의의 법정 선택";
            readyText.text=(host?Session.HostReady:Session.GuestReady)?"준비 취소":"준비 완료";
            ReadyButton.interactable=preparing && Network.CanReady && Session.MapId=="court";
            TopicInput.interactable=keywordButton.interactable=configuring && Network.CanReady && !Session.AiGenerating && !generated;
            RoundInput.interactable=configuring && host && Network.CanReady;
            SubmitButton.gameObject.SetActive(host);
            SubmitButton.interactable=configuring && host && Network.CanReady && !Session.AiGenerating && Session.HostKeyword.Length>0 && Session.GuestKeyword.Length>0;
            counter.text=$"Host: {(Session.HostKeyword.Length>0?Session.HostKeyword:"입력 대기")}\nGuest: {(Session.GuestKeyword.Length>0?Session.GuestKeyword:"입력 대기")}";
            errorText.text=Session.AiGenerating?"여신이 사건을 만들고 있습니다…":Session.AiError;
            summary.text=generated?$"[사건 개요]\n{Session.CaseSummary}\n\n[공통 질문]\n{Session.CommonQuestion}":$"맵: 정의의 법정\n{Session.Topic}\n\n최대 {Session.MaxRound}라운드";
            if(!online && frontPage==FrontPage.Join)RefreshRooms();
            ApplyRole();
        }
        private void BuildUI()
        {
            var canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 20;
            var scaler = gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280,720); scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            worldHint=Node("WorldHint",transform);
            var hintRect=worldHint.GetComponent<RectTransform>();hintRect.anchorMin=hintRect.anchorMax=new Vector2(0.5f,1);hintRect.pivot=new Vector2(0.5f,1);hintRect.sizeDelta=new Vector2(760,50);
            var hint=Text(worldHint.transform,"마우스로 둘러보기  ·  Esc: 설정 / 친구 초대 / 준비",20,50,Color.white);Stretch(hint.rectTransform);hint.alignment=TextAlignmentOptions.Center;
            voiceHint=hint;hint.fontSize=17;
            var transcript=Node("SttTranscript",worldHint.transform);
            var transcriptRect=transcript.GetComponent<RectTransform>();transcriptRect.anchorMin=transcriptRect.anchorMax=new Vector2(0.5f,1);transcriptRect.pivot=new Vector2(0.5f,1);transcriptRect.anchoredPosition=new Vector2(0,-60);transcriptRect.sizeDelta=new Vector2(880,100);
            transcriptLabel=Text(transcript.transform,"",19,100,Color.white);Stretch(transcriptLabel.rectTransform);transcriptLabel.alignment=TextAlignmentOptions.Top;
            caseHud=Node("GoddessCase",transform);var caseRect=caseHud.GetComponent<RectTransform>();caseRect.anchorMin=new Vector2(0.12f,0);caseRect.anchorMax=new Vector2(0.88f,0);caseRect.pivot=new Vector2(0.5f,0);caseRect.anchoredPosition=new Vector2(0,18);caseRect.sizeDelta=new Vector2(0,210);
            caseHud.AddComponent<UnityEngine.UI.Image>().color=new Color(0.025f,0.04f,0.065f,0.94f);
            goddessText=Text(caseHud.transform,"",18,210,new Color(0.94f,0.87f,0.7f));Stretch(goddessText.rectTransform);goddessText.rectTransform.offsetMin=new Vector2(18,12);goddessText.rectTransform.offsetMax=new Vector2(-18,-12);goddessText.enableAutoSizing=true;goddessText.fontSizeMin=12;goddessText.fontSizeMax=18;
            caseHud.SetActive(false);
            backdrop = Node("Backdrop", transform);
            Stretch(backdrop.GetComponent<RectTransform>());
            backdrop.AddComponent<UnityEngine.UI.Image>().color = new Color(0.025f,0.035f,0.065f,0.82f);
            var panel = Node("LobbyCard",backdrop.transform);
            var rect = panel.GetComponent<RectTransform>();
            cardRect=rect;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f,0.5f); rect.sizeDelta = new Vector2(820,680);
            panel.AddComponent<UnityEngine.UI.Image>().color = new Color(0.055f,0.075f,0.11f,1);
            var layout = panel.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.padding = new RectOffset(32,32,12,12); layout.spacing = 6;
            layout.childControlHeight = layout.childControlWidth = true; layout.childForceExpandHeight = false;
            Text(panel.transform,"JUSTITIA  /  정의의 여신",16,20,new Color(0.86f,0.69f,0.38f));
            heading = Text(panel.transform,"게임 준비",28,36,Color.white);
            status = Text(panel.transform,"",16,28,new Color(0.7f,0.79f,0.88f));
            connectionStatus=Text(panel.transform,"",15,54,new Color(0.7f,0.79f,0.88f));
            connectionToolbar=Group("ConnectionToolbar",panel.transform,48,true);
            inviteButton=Button(connectionToolbar.transform,"친구 초대",OpenInvites);
            Button(connectionToolbar.transform,"음성 설정",()=>{friendList.SetActive(false);voicePanel.SetActive(true);Refresh();});
            resumeButton=Button(connectionToolbar.transform,"맵으로 돌아가기",()=>SetMenuOpen(false));
            Button(connectionToolbar.transform,"나가기",()=>{if(Network.IsSteam || SteamRoom.Busy)SteamRoom.Leave();else Network.Leave();});
            connection=Group("Connect",panel.transform,340,false);
            var tabs=Group("ConnectionMode",connection.transform,48,true);
            steamTab=Button(tabs.transform,"Steam",()=>{steamMode=true;Refresh();});
            lanTab=Button(tabs.transform,"LAN",()=>{steamMode=false;Refresh();});
            steamConnect=Group("SteamConnect",connection.transform,275,false);
            Text(steamConnect.transform,"친구가 만든 방에 참가하세요.",21,36,Color.white);
            var steamRow=Group("SteamActions",steamConnect.transform,48,true);
            steamHost=Button(steamRow.transform,"친구 방 새로고침",()=>SteamRoom.RefreshFriendRooms());
            var rooms=Node("RoomList",steamConnect.transform);Height(rooms,170);
            var roomsScroll=rooms.AddComponent<UnityEngine.UI.ScrollRect>();roomsScroll.horizontal=false;roomsScroll.scrollSensitivity=30;
            var roomsViewport=Node("Viewport",rooms.transform);Stretch(roomsViewport.GetComponent<RectTransform>());roomsViewport.AddComponent<UnityEngine.UI.Image>().color=new Color(0.08f,0.11f,0.16f);roomsViewport.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic=true;
            var roomsContent=Node("Content",roomsViewport.transform);roomListContent=roomsContent.transform;
            var roomsRect=roomsContent.GetComponent<RectTransform>();roomsRect.anchorMin=new Vector2(0,1);roomsRect.anchorMax=Vector2.one;roomsRect.pivot=new Vector2(0.5f,1);roomsRect.sizeDelta=Vector2.zero;
            var roomsLayout=roomsContent.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();roomsLayout.childControlWidth=true;roomsLayout.childControlHeight=true;roomsLayout.childForceExpandHeight=false;roomsLayout.spacing=8;
            roomsContent.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit=UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            roomsScroll.content=roomsRect;roomsScroll.viewport=roomsViewport.GetComponent<RectTransform>();
            lanConnect=Group("LANConnect",connection.transform,275,false);
            Text(lanConnect.transform,"Host는 방을 만들고, Guest는 Host의 IP로 참가하세요.",21,52,Color.white);
            Text(lanConnect.transform,"Host IP (같은 PC에서 테스트할 때는 127.0.0.1)",17,30,new Color(0.7f,0.79f,0.88f));
            addressInput=Input(lanConnect.transform,"127.0.0.1",48,false,15);addressInput.text="127.0.0.1";
            var connectRow=Group("ConnectActions",lanConnect.transform,48,true);
            lanHost=Button(connectRow.transform,"방 만들기 (Host)",()=>Network.Host());
            lanJoin=Button(connectRow.transform,"참가하기 (Guest)",()=>Network.Join(addressInput.text.Trim()));
            Text(lanConnect.transform,"내 LAN IP: "+LanAddresses()+"\nUDP 포트: 7777",16,60,new Color(0.7f,0.79f,0.88f));

            var friendScroll=Node("SteamFriends",panel.transform);Height(friendScroll,300);
            var friendScroller=friendScroll.AddComponent<UnityEngine.UI.ScrollRect>();friendScroller.horizontal=false;
            friendScroll.AddComponent<UnityEngine.UI.RectMask2D>();friendScroll.AddComponent<UnityEngine.UI.Image>().color=new Color(0.1f,0.13f,0.18f);
            friendList=friendScroll;
            var friendContent=Node("Content",friendScroll.transform);
            var friendRect=friendContent.GetComponent<RectTransform>();friendRect.anchorMin=new Vector2(0,1);friendRect.anchorMax=Vector2.one;friendRect.pivot=new Vector2(0.5f,1);friendRect.sizeDelta=Vector2.zero;
            var friendLayout=friendContent.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();friendLayout.childControlHeight=true;friendLayout.childControlWidth=true;friendLayout.childForceExpandHeight=false;friendLayout.spacing=8;
            friendContent.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit=UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            friendScroller.content=friendRect;friendScroller.viewport=friendScroll.GetComponent<RectTransform>();friendScroller.scrollSensitivity=30;
            friendList.SetActive(false);
            voicePanel=Group("VoiceSettings",panel.transform,310,false);
            voiceStatus=Text(voicePanel.transform,"",18,48,Color.white);
            muteLabel=Button(voicePanel.transform,"마이크 끄기",()=>Voice.SetMuted(!Voice.Muted)).GetComponentInChildren<TMP_Text>();
            volumeLabel=Text(voicePanel.transform,"",20,36,Color.white);
            var volumeRow=Group("VoiceVolume",voicePanel.transform,48,true);
            Button(volumeRow.transform,"볼륨 −",()=>Voice.SetVolume(Voice.Volume-0.1f));
            Button(volumeRow.transform,"볼륨 +",()=>Voice.SetVolume(Voice.Volume+0.1f));
            Text(voicePanel.transform,"맵에서 V를 누르고 말하기 → 놓으면 한국어 STT 출력\nSteam 입력 장치 사용 · 혼자 Host로도 STT 테스트 가능",16,56,new Color(0.7f,0.79f,0.88f));
            Button(voicePanel.transform,"설정 닫기",()=>{voicePanel.SetActive(false);if(!wasOnline)frontPage=FrontPage.Main;Refresh();});
            voicePanel.SetActive(false);
            preparation = Group("Preparation",panel.transform,245,false);
            mapStatus=Text(preparation.transform,"",23,44,Color.white);
            mapButton=Button(preparation.transform,"법정 맵 선택",()=>Network.SelectMap("court"));
            Text(preparation.transform,"Host가 맵을 선택한 뒤 양쪽 모두 준비해 주세요.\n준비가 끝나면 각자 키워드를 제출해 여신의 사건을 받습니다.",17,72,new Color(0.7f,0.79f,0.88f));
            ReadyButton = Button(preparation.transform,"준비 완료",ToggleReady);
            readyText = ReadyButton.GetComponentInChildren<TMP_Text>();

            setup = Group("HostSetup",panel.transform,310,false);
            Text(setup.transform,"내 사건 키워드 (1~40자)",20,28,Color.white);
            TopicInput = Input(setup.transform,"예: 아이콘 / 리모콘",48,false,40);
            keywordButton=Button(setup.transform,"내 키워드 제출",()=>{if(!Network.SetKeyword(TopicInput.text))errorText.text="키워드는 1~40자로 입력해 주세요.";});
            counter = Text(setup.transform,"",16,48,new Color(0.7f,0.79f,0.88f));
            errorText = Text(setup.transform,"",16,40,new Color(1,0.75f,0.5f));
            SubmitButton = Button(setup.transform,"여신에게 사건 생성 요청",()=>GoddessAI.Generate());

            waiting = Group("GuestWaiting",panel.transform,290,false);
            Text(waiting.transform,"양쪽 모두 준비 완료",26,60,Color.white);
            Text(waiting.transform,"Host가 주제와 최대 라운드를 작성하고 있습니다.\n설정을 제출할 때까지 기다려 주세요.",22,130,new Color(0.7f,0.79f,0.88f));
            submitted = Group("Submitted",panel.transform,310,false);
            Text(submitted.transform,"정의의 여신 · 사건과 공통 질문 (스크롤)",19,30,new Color(0.4f,0.88f,0.73f));
            var scrollNode = Node("SummaryScroll",submitted.transform); Height(scrollNode,150);
            var scroll = scrollNode.AddComponent<UnityEngine.UI.ScrollRect>(); scroll.horizontal=false;
            var viewport = Node("Viewport",scrollNode.transform); Stretch(viewport.GetComponent<RectTransform>()); viewport.AddComponent<UnityEngine.UI.RectMask2D>();
            viewport.AddComponent<UnityEngine.UI.Image>().color=new Color(0,0,0,0.05f);
            summary=Text(viewport.transform,"",18,200,Color.white);
            var summarySize=summary.GetComponent<UnityEngine.UI.LayoutElement>();summarySize.preferredHeight=-1;summarySize.minHeight=-1;
            var content=summary.rectTransform; content.anchorMin=new Vector2(0,1); content.anchorMax=Vector2.one;content.pivot=new Vector2(0.5f,1); content.sizeDelta=new Vector2(0,200);
            summary.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>().verticalFit=UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport=viewport.GetComponent<RectTransform>();scroll.content=content;scroll.movementType=UnityEngine.UI.ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=30;
            roundSettings=Group("RoundSetting",submitted.transform,48,true);
            Text(roundSettings.transform,"최대 라운드 (1~5)",18,46,Color.white);
            RoundInput = Input(roundSettings.transform,"3",46,false,2);RoundInput.text="3";RoundInput.contentType=TMP_InputField.ContentType.IntegerNumber;
            startCaseButton=Button(submitted.transform,"이 사건으로 게임 시작",SubmitSettings);
            lobbyVoice=Text(panel.transform,"",14,48,new Color(0.6f,0.8f,0.78f));lobbyVoice.overflowMode=TextOverflowModes.Ellipsis;
            frontBack=Button(panel.transform,"메인 메뉴",ShowMainMenu).gameObject;
            BuildFrontMenu();
        }

        private void BuildFrontMenu()
        {
            mainMenu=Node("MainMenu",transform);Stretch(mainMenu.GetComponent<RectTransform>());
            mainMenu.AddComponent<UnityEngine.UI.Image>().color=new Color(0.025f,0.04f,0.065f,1);
            var frame=Node("LayoutFrame",mainMenu.transform);mainFrame=frame.GetComponent<RectTransform>();mainFrame.anchorMin=mainFrame.anchorMax=mainFrame.pivot=new Vector2(0.5f,0.5f);mainFrame.sizeDelta=new Vector2(1280,720);
            var brand=Text(frame.transform,"JUSTITIA",20,32,new Color(0.78f,0.64f,0.4f));Place(brand.rectTransform,new Vector2(80,-75),new Vector2(400,32));
            var title=Text(frame.transform,"정의의\n여신",76,200,Color.white);Place(title.rectTransform,new Vector2(74,-125),new Vector2(430,210));
            var sub=Text(frame.transform,"당신의 이야기를 저울 위에 올려놓으세요.",17,40,new Color(0.58f,0.66f,0.75f));Place(sub.rectTransform,new Vector2(80,-345),new Vector2(460,40));
            var buttons=Group("MainActions",frame.transform,240,false);Place(buttons.GetComponent<RectTransform>(),new Vector2(80,-415),new Vector2(300,230));
            createRoomButton=Button(buttons.transform,"방 만들기",CreateRoom);
            Button(buttons.transform,"방 참가",ShowJoinMenu);Button(buttons.transform,"설정",ShowSettings);Button(buttons.transform,"종료",QuitGame);
            foreach(var button in buttons.GetComponentsInChildren<UnityEngine.UI.Button>())
            {button.GetComponent<UnityEngine.UI.Image>().color=new Color(0.09f,0.13f,0.18f);button.GetComponentInChildren<TMP_Text>().color=new Color(0.92f,0.86f,0.74f);var outline=button.gameObject.AddComponent<UnityEngine.UI.Outline>();outline.effectColor=new Color(0.6f,0.49f,0.3f,0.5f);outline.effectDistance=new Vector2(1,-1);}
            mainStatus=Text(frame.transform,"",16,54,new Color(0.65f,0.76f,0.82f));var sr=mainStatus.rectTransform;sr.anchorMin=sr.anchorMax=sr.pivot=Vector2.one;sr.anchoredPosition=new Vector2(-50,-35);sr.sizeDelta=new Vector2(580,54);mainStatus.alignment=TextAlignmentOptions.TopRight;
            var art=Node("ScalesEmblem",frame.transform);Place(art.GetComponent<RectTransform>(),new Vector2(660,-190),new Vector2(440,360));
            Bar(art.transform,new Vector2(216,-40),new Vector2(8,270));Bar(art.transform,new Vector2(35,-90),new Vector2(370,6));
            Bar(art.transform,new Vector2(65,-95),new Vector2(3,130));Bar(art.transform,new Vector2(365,-95),new Vector2(3,130));
            Bar(art.transform,new Vector2(15,-225),new Vector2(110,5));Bar(art.transform,new Vector2(315,-225),new Vector2(110,5));Bar(art.transform,new Vector2(150,-312),new Vector2(140,7));
            var caption=Text(art.transform,"두 사람의 진술, 하나의 판결",20,40,new Color(0.67f,0.59f,0.45f));Place(caption.rectTransform,new Vector2(40,-360),new Vector2(400,40));
            var foot=Text(frame.transform,"2인 온라인 토론 게임  /  Steam",14,26,new Color(0.4f,0.49f,0.58f));Place(foot.rectTransform,new Vector2(660,-650),new Vector2(450,26));
        }
        private void Place(RectTransform rect,Vector2 position,Vector2 size)
        {rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=position;rect.sizeDelta=size;}
        private void Bar(Transform parent,Vector2 position,Vector2 size)
        {var bar=Node("GoldLine",parent);Place(bar.GetComponent<RectTransform>(),position,size);bar.AddComponent<UnityEngine.UI.Image>().color=new Color(0.56f,0.43f,0.23f);}

        private GameObject Node(string name,Transform parent)
        { var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return go; }
        private void Stretch(RectTransform rect)
        {rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
        private void Height(GameObject go,float value)
        {var element=go.AddComponent<UnityEngine.UI.LayoutElement>();element.preferredHeight=value;element.minHeight=value;}
        private GameObject Group(string name,Transform parent,float height,bool horizontal)
        {
            var go=Node(name,parent);Height(go,height);
            UnityEngine.UI.HorizontalOrVerticalLayoutGroup layout=horizontal ? go.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>() : go.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.spacing=8;layout.childControlHeight=layout.childControlWidth=true;layout.childForceExpandHeight=false;return go;
        }
        private TMP_Text Text(Transform parent,string value,int size,float height,Color color)
        {
            var go=Node("Label",parent);Height(go,height);var text=go.AddComponent<TextMeshProUGUI>();
            text.font=font;text.text=value;text.fontSize=size;text.color=color;text.richText=false;text.raycastTarget=false;
            text.alignment=TextAlignmentOptions.MidlineLeft;return text;
        }
        private UnityEngine.UI.Button Button(Transform parent,string title,UnityEngine.Events.UnityAction action)
        {
            var go=Node(title,parent);Height(go,48);go.AddComponent<UnityEngine.UI.Image>().color=new Color(0.79f,0.61f,0.3f);
            var button=go.AddComponent<UnityEngine.UI.Button>();button.onClick.AddListener(action);
            button.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};
            var text=Text(go.transform,title,20,48,new Color(0.035f,0.045f,0.07f));Stretch(text.rectTransform);text.alignment=TextAlignmentOptions.Center;
            return button;
        }
        private TMP_InputField Input(Transform parent,string hint,float height,bool multiline,int limit)
        {
            var go=Node("Input",parent);Height(go,height);go.AddComponent<UnityEngine.UI.Image>().color=new Color(0.12f,0.16f,0.22f);
            var input=go.AddComponent<TMP_InputField>();
            var viewport=Node("TextArea",go.transform);Stretch(viewport.GetComponent<RectTransform>());
            viewport.GetComponent<RectTransform>().offsetMin=new Vector2(14,8);viewport.GetComponent<RectTransform>().offsetMax=new Vector2(-14,-8);
            viewport.AddComponent<UnityEngine.UI.RectMask2D>();
            var text=Text(viewport.transform,"",22,height,Color.white);Stretch(text.rectTransform);text.alignment=TextAlignmentOptions.TopLeft;
            var placeholder=Text(viewport.transform,hint,20,height,new Color(0.55f,0.63f,0.73f));Stretch(placeholder.rectTransform);placeholder.alignment=TextAlignmentOptions.TopLeft;
            input.textViewport=viewport.GetComponent<RectTransform>();input.textComponent=(TextMeshProUGUI)text;input.placeholder=placeholder;
            input.lineType=multiline?TMP_InputField.LineType.MultiLineNewline:TMP_InputField.LineType.SingleLine;
            input.characterLimit=limit;input.richText=false;return input;
        }
        private void OnDestroy()
        {
            if(Network)Network.Changed-=Refresh;
            if(SteamRoom)SteamRoom.Changed-=Refresh;
            if(SeatView)SeatView.UiInputActive=false;
            if(PreviewControls)PreviewControls.SetActive(previousPreviewActive);
            if(font && font!=FallbackFont)
            {
                foreach(var atlas in font.atlasTextures) if(atlas)Destroy(atlas);
                if(font.material)Destroy(font.material);
                Destroy(font);
            }
        }

        private static string LanAddresses()
        {
            var addresses=new System.Collections.Generic.List<string>();
            foreach(var adapter in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if(adapter.OperationalStatus!=System.Net.NetworkInformation.OperationalStatus.Up)continue;
                foreach(var address in adapter.GetIPProperties().UnicastAddresses)
                    if(address.Address.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(address.Address))
                        addresses.Add(address.Address.ToString());
            }
            return addresses.Count==0?"127.0.0.1":string.Join(", ",addresses);
        }
    }
}
