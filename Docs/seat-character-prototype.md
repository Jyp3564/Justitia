# Host / Guest 캐릭터 프로토타입

## 실행

`Assets/Scenes/SampleScene.unity`에서 Play를 누른다.
현재 SampleScene은 네트워크 로비를 사용한다. 방 만들기는 Host, 참가는 Guest 시점으로 고정한다.
아래 Overview/Reset view 버튼은 로비 도입 이전의 캐릭터 미리보기 도구이며 로비 실행 중에는 숨겨진다.
캐릭터 시점에 들어가면 커서가 자동으로 잠기고, 클릭이나 드래그 없이 마우스를 움직이는 것만으로 둘러본다.
UI 입력 또는 연출 잠금이 활성화되거나 창이 포커스를 잃으면 커서를 해제하고 시점 입력을 중지한다.
캐릭터 시점에서는 커서가 잠기므로 미리보기 버튼은 UI 입력 상태를 활성화한 경우에 사용할 수 있다.
Overview는 전체 보기, Reset view는 선택한 좌석의 기본 시선으로 복귀한다.
키보드 입력, 걷기, 점프는 사용하지 않는다.

## 구성

- Guest: 월드 왼쪽, 청록색. Host: 월드 오른쪽, 주황색.
- `Assets/Players/Prefabs/GuestCharacter.prefab`, `HostCharacter.prefab`.
- 각 프리팹을 배치할 때 `SeatPlayer.SeatAnchor`를 해당 좌석에 연결한다.
- SeatPlayer는 좌석 위치와 회전을 따르고 물리 이동 컨트롤러를 사용하지 않는다.
- MouseSeatView는 좌석 기준 yaw ±100°, pitch ±60°로 제한한다.
- UiInputActive / CinematicLocked는 이후 UI 및 연출 상태에서 연결할 잠금 API다.
- 로컬 1인칭에서는 본인 큐브 렌더러만 숨긴다.
- 로컬 역할 미리보기는 유지되어 있지만 현재 로비에서는 사용하지 않는다. 실제 LAN 방 생성/참가는 `Docs/network-lan.md`를 참고한다.
- 천칭과 여신 도형은 위치 확인용이며 판정 애니메이션은 아직 없다.

## 확인 결과

Unity 6000.5.6f1, 에디터 Play Mode에서 확인:

- 스크립트 컴파일 성공, 런타임 오류 없음.
- 역할과 좌석 매핑, 큐브 프리팹 인스턴스 및 네 개 버튼 이벤트 연결 확인.
- 양·음 회전 제한, UI/연출 시점 잠금, 좌석 이동 추종 검증 통과.
- Guest/Host 전환 시 로컬 캐릭터 숨김과 카메라 EyeAnchor 추종 검증 통과.
- 전체 보기와 1인칭 화면 렌더 확인.

버튼 검증은 onClick 호출, 회전 검증은 입력 델타 주입으로 실행했다.
실제 마우스 하드웨어 입력 및 패키징 빌드, 2인 네트워크 테스트는 수행하지 않았다.
