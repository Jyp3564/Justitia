# 준비 및 Host 설정 화면

SampleScene의 LobbyUI에서 실행한다. Unity uGUI로 구성되며
`Assets/UI/Lobby/LobbyUI.prefab`이 런타임에 화면을 생성한다.

## 현재 흐름

1. Host 준비 완료. 혼자 준비한 경우 대기하며 준비 취소 가능.
2. Guest 준비 완료. 양쪽이 준비하면 자동으로 HostSetup 단계로 진입.
3. Host는 주제(공백 불가, 최대 1,000자)와 최대 라운드(정수 1~5, 기본 3)를 입력.
4. Guest에게는 Host 설정 대기 화면 표시.
5. Host 제출 시 설정 확정 및 양쪽 제출 완료 화면 표시. 중복 제출/변경 거부.

사용자의 최신 요청에 따라 원본 기획서의 '설정 후 준비' 순서를
'양쪽 준비 후 Host 설정'으로 바꿨다. 실제 발언 턴 진행은 이번 작업에 포함하지 않았다.

## 네트워크 테스트

Play 또는 Windows 실행 파일에서 Host는 '방 만들기', Guest는 Host의 IPv4 주소를 입력해 '참가하기'를 누른다.
같은 PC에서는 127.0.0.1, 다른 PC의 같은 LAN에서는 Host의 LAN IPv4 주소를 사용한다. UDP 포트는 7777이다.
역할은 연결 방식으로 고정되며 로컬 역할 전환 버튼은 제거했다.
두 명이 연결되면 각자 준비하고, Host가 설정을 입력해 제출한다.
텍스트 입력에는 키보드를 사용하며 UI 조작 중 마우스 시점은 잠긴다.

LobbySession은 Host가 관리하고 Guest는 연결을 통해 전달받은 상태 스냅샷을 적용한다.
LanLobbyConnection은 Unity Netcode for GameObjects와 Unity Transport의 LAN 직접 연결을 사용한다.
Guest 준비 요청은 연결 ID로 역할을 확인한다. Guest가 임의로 Host 역할을 선택하거나 설정을 제출할 수 없다.
방은 Host 1명과 Guest 1명으로 제한하며 시작 이후 추가 접속을 거부한다.
상대 이탈 시 판을 중단하고 연결 화면으로 돌아간다. 재접속/Host 이관/인터넷 초대 코드는 아직 지원하지 않는다.
스냅샷의 sessionId와 revision으로 이전 판·중복·역순 상태 적용을 차단한다.
신뢰성 있는 순서 보장 전송 및 메시지 분할로 긴 한글 주제를 전달한다.
LobbySession.Changed 이벤트와 Phase/Topic/MaxRound를 후속 진행 코드에 연결할 수 있다.
한국어 글꼴은 Windows에 설치된 Malgun Gothic을 TMP DynamicOS로 사용한다.
다른 OS를 지원하려면 배포 가능한 한국어 폰트 에셋을 추가해야 한다.

## 검증

Unity 6000.5.6f1 Play Mode, MCP를 통한 버튼 이벤트 호출 및 상태 검증:

- 준비 전 제출, 한 명만 준비한 상태의 시작을 거부.
- 준비 취소, 두 번째 준비 완료 시 자동 전환.
- Guest에게 Host 폼 숨김 및 제출 API에서 Guest 거부.
- 공백 주제, 1,001자 주제 거부; 1,000자 주제 허용.
- 라운드 0, 6, -1, 소수, 문자, 빈 문자열 거부; 1, 3, 5 허용.
- 설정 확정 후 재제출 및 준비 상태 변경 거부.
- Host 버튼 제출에서 완료 상태로 전환하고 Guest 시점에서도 동일 설정 확인.
- 준비/설정 화면의 한글 렌더 확인, UI 입력 중 시점 잠금 확인.

최초 UI 검증 기록이며, 후속 네트워크 빌드 검증은 `network-lan.md`를 참고한다.
