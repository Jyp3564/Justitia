# Steam 개발 연동

## 실행

1. 서로 다른 PC에서 서로 다른 Steam 계정으로 로그인한다.
2. 두 사람 모두 `Builds/Steam` 폴더 전체를 받고 `Justitia.exe`를 실행한다.
3. Host: 메인 메뉴 → 방 만들기 → 대기 로비 → 법정 맵 선택 → 친구 초대.
4. Guest: 메인 메뉴 → 방 참가 → 친구 방 선택 또는 Steam 초대 수락 → 대기 로비.
5. 각자 로비에서 준비 완료 → Host 주제/최대 라운드 제출 → 양쪽 맵 입장. 양쪽 모두 프로토콜 v2 새 빌드를 사용한다.

오버레이를 사용할 수 없거나 초대 창이 나타나지 않으면 게임 내 Steam 친구 목록에서 초대한다. 실제 초대는 사용자가 친구 버튼을 눌렀을 때만 전송된다. Esc 또는 맵으로 돌아가기로 메뉴를 닫으면 마우스 시점 조작이 재개된다.
한 PC의 같은 Steam 계정 두 프로세스는 독립된 두 사용자가 아니다. 기존 LAN 테스트는 LAN 탭에서 가능하다.
프로그램이 꺼져 있을 때 Steam이 전달하는 `+connect_lobby` 실행 인자도 처리한다.

## 현재 App ID

사용자 선택에 따라 테스트용 Spacewar App ID **480**을 사용한다.
`Assets/Resources/SteamConfig.json`에서 설정을 관리한다.
정식 App ID를 받으면 JSON 및 프로젝트 루트 `steam_appid.txt`를 같은 번호로 바꾼다.
개발 빌드는 실행 파일 옆에 설정된 번호의 `steam_appid.txt`를 자동 생성한다.
480으로 비개발 빌드를 시도하면 빌드를 차단한다. 정식 빌드에서는 테스트용 파일을 배포하지 않는다.
이 파일은 비밀 키가 아니며 Publisher Key나 Web API Key는 프로젝트에 넣지 않는다.

## 구현

- Steamworks.NET 2025.164.1 (공식 저장소 고정 태그, MIT).
- Steam 로그인 초기화와 메인 스레드 콜백 처리, 종료 시 정리.
- 친구 전용 Steam 로비, 최대 2명, 게임/프로토콜/원래 Host 메타데이터 검증.
- 친구 초대 UI, 초대 수락 콜백, 게임 내 친구 목록 초대, 게임 시작 시 전달된 로비 참가.
- SteamNetworkingSockets P2P 연결을 사용하는 NGO NetworkTransport.
- 현재 로비의 실제 Steam 사용자만 Host 소켓에 연결 가능. 클라이언트가 주장한 역할을 신뢰하지 않는다.
- 기존 Host 권위 준비/설정/스냅샷 로직을 그대로 재사용한다.
- Guest 접속 또는 준비 단계 종료 시 로비 추가 참가를 잠근다.
- Steam이 로비 소유자를 자동 변경해도 게임은 Host 이관하지 않고 중단한다.
- 로비 생성/참가 요청 시간 제한 및 취소 뒤 늦게 도착한 성공 결과의 로비 퇴장 처리.
- TCP/IP 주소 교환 없이 Steam 사용자 식별자로 연결하며 Valve 네트워크 접근을 초기화한다.
- 모든 게임 메시지는 순서가 보장되는 신뢰성 전송으로 보낸다. 이 게임의 턴 기반 메시지를 위한 선택이다.

## 검증 범위

- Unity 스크립트 컴파일 통과.
- 실제 Steam API 초기화 및 App ID 480 일치 확인.
- 실제 Steam 친구 전용 2인 로비 생성, 게임 식별 메타데이터와 최대 인원 확인.
- Guest가 없으면 준비 불가, 로비 나가기/소켓 정리 확인.
- Steam 네이티브 소켓 쌍과 작성한 NGO 전송 계층을 통해 약 12KB 한글 데이터 정확히 송수신.
- Windows Steam 개발 빌드 성공(오류 0), steam_api64.dll과 개발용 steam_appid.txt 포함 확인.
- 새 빌드의 두 독립 프로세스로 LAN 준비/설정 동기화와 Guest 종료 처리 회귀 테스트 통과.
- 두 Steam 계정의 원격 P2P, 다른 인터넷 환경, 친구 초대 수락/오버레이 및 실행 인자는 아직 실사용 검증 전.

검증 기록은 `TestResults/Steam/integration-report.json`, `lan-host.json`, `lan-guest.json`에 있다.

이 단계는 Steam 멀티플레이어 개발 연동이다. Steam 상점 등록/배포, 실제 App ID 소유권 검증,
Backend의 Steam ticket 인증은 포함하지 않는다.

## 참고

- https://github.com/rlabrecque/Steamworks.NET/tree/2025.164.1
- https://partner.steamgames.com/doc/api/ISteamNetworkingSockets
- https://partner.steamgames.com/doc/features/multiplayer/matchmaking
- https://partner.steamgames.com/doc/sdk/api
