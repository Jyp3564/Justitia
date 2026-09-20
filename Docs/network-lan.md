# LAN 로비 동기화

## 사용법

1. Host: SampleScene에서 Play 또는 `Builds/Windows/Justitia.exe` 실행 → 방 만들기.
2. Guest: 별도 실행 파일 실행 → Host IP 입력 → 참가하기.
3. 같은 PC에서는 `127.0.0.1`, 같은 LAN의 다른 PC에서는 Host의 LAN IPv4 주소를 사용한다.
4. 각자 준비 완료 → Host 주제와 최대 라운드 입력 → 설정 제출.
5. 양쪽에서 동일한 제출 결과 확인. 나가기를 누르면 상대에게 중단 안내.

포트는 UDP 7777. 다른 PC 접속 시 Windows 방화벽에서 해당 앱의 개인 네트워크 통신이 허용되어야 한다.
이 작업은 방화벽 규칙이나 공유기 포트 설정을 변경하지 않는다.
다른 PC에는 exe 한 개가 아니라 `Builds/Windows` 폴더 전체를 복사한다.
인터넷 초대 코드, Steam 인증, 암호화된 인터넷 통신, 재접속 복구와 Host 이관은 범위 밖이다.

## 설계

- Netcode for GameObjects 2.13.2 / Unity Transport, 직접 IPv4 연결.
- 공유 월드는 동일 SampleScene을 사용한다. 고정 캐릭터의 위치는 기존 좌석에 있으며 네트워크 플레이어 오브젝트는 생성하지 않는다.
- Host가 유일한 상태 작성자. Guest는 준비 요청만 보낸다.
- 연결 승인에서 프로토콜 일치 및 2인 제한을 검사한다.
- 확정 주제/최대 라운드를 전송한다. 아직 제출하지 않은 Host 입력 초안은 로컬에만 존재한다.
- ReliableFragmentedSequenced 메시지로 sessionId, revision, 준비 상태, phase, topic, maxRound를 공유한다.
- Guest 요청의 역할은 메시지 내용이 아니라 연결 ID로 확인한다. 요청 번호 중복/이전 판 요청을 무시한다.
- Guest는 이전 sessionId, 중복/역순 revision, 잘못된 상태와 범위 값을 거부한다.
- 연결/초기 동기화 시간 초과는 12초, 통신 끊김 감지는 약 6초. 상대 종료 시 중단하고 입력 잠금.
- 새 연결에서 새 모델/sessionId를 만들고 이전 데이터와 준비 상태를 폐기한다.
- 실제 토론 턴/AI 호출은 이번 네트워크 단계의 범위 밖이다.

## 자동 검증 재실행

프로젝트 빌드 후 두 프로세스에서 다음 명령을 각각 실행한다. 보고서 경로는 쓰기 가능한 절대 경로로 바꾼다.

```text
Justitia.exe -batchmode -nographics --network-smoke host C:/path/host-report.json 17777
Justitia.exe -batchmode -nographics --network-smoke guest C:/path/guest-report.json 17777
```

테스트는 별도 Host/Guest 프로세스로 접속하고 각자 준비한 다음, Host가 긴 한글 주제와 5라운드를 제출한다.
Guest는 동일 내용 수신과 Host 전용 제출 거부를 검증한 뒤 종료한다. Host는 Guest 이탈에 따른 중단을 검증한다.
성공/실패는 각 보고서의 passed 필드로 확인한다. 일반 실행에서는 테스트 코드가 동작하지 않는다.

## 검증 결과 (2026-09-20)

Unity 6000.5.6f1 / Windows, 같은 PC의 독립 프로세스:

- Windows 개발 빌드 성공, 오류 0개.
- 패키징된 Host + 패키징된 Guest: 접속, 양쪽 준비, Host 설정 제출 통과.
- 977자 한글 주제와 최대 5라운드를 Guest가 정확히 수신 (revision 3).
- Guest의 Host 설정 제출 시도 거부.
- Guest 종료 시 Host 게임 중단 통과.
- 에디터 Host + 패키징 Guest 연결 중 세 번째 실행 파일 참가 거부, 기존 Guest 연결 유지.
- 에디터 UI의 준비·주제 제출을 통해 패키징 Guest에 동일 설정 전달.
- 패키징 Host를 강제 종료했을 때 에디터 Guest가 끊김을 감지하고 Offline 전환, 준비/제출 비활성화.
- 스냅샷 중복/역순 revision, 이전 sessionId, 잘못된 라운드 값 거부.

보고서: `TestResults/Network/host-report.json`, `guest-report.json`, `capacity-guest-report.json`,
`third-client-report.json`, `host-disconnect-report.json`.

별도 LAN PC 간 연결, 실제 하드웨어 UI 입력, 인터넷 환경 및 Steam 연동은 아직 검증하지 않았다.
헤드리스 테스트 로그의 Unity 진단 서버 접속 실패는 LAN 동기화와 별개이며, 실제 테스트 보고서는 모두 통과했다.
