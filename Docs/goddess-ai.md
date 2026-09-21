# 여신 사건 생성 AI 연동

원본: C:/Users/user/Documents/Codex/2026-09-21/referenced-chatgpt-conversation-this-is-an/outputs/justice_case_mvp/main.py

## 게임 사용 순서

메인 메뉴 → 방 만들기/참가 → Host 맵 선택 → 양쪽 준비 완료.
각 플레이어는 자기 키워드(1~40자)를 입력하고 제출한다.
두 키워드가 모이면 Host가 여신에게 사건 생성 요청을 누른다.
두 사람에게 동일한 사건 개요와 공통 질문을 표시한다.
Host가 최대 라운드(1~5)를 정하고 이 사건으로 게임 시작을 누른다.
맵에서는 정의의 여신 패널에 사건과 질문을 계속 표시한다. V 키 음성/STT는 기존 기능을 유지한다.

## 실행 구조

Unity Host → Tools/JusticeAI/bridge.py → 원본 main.py의 generate_case() → OpenAI API.
키워드와 결과만 JSON으로 전달하며, 원본 main.py의 CLI 입력 및 results 파일 저장은 실행하지 않는다.
Guest의 키워드는 승인된 Guest 연결로만 받는다. AI 생성과 결과 확정은 Host가 담당한다.
생성 중에는 키워드 변경·중복 생성을 막고, 실패는 동기화된 오류 문구와 명시적 재시도로 처리한다.
접속 종료 시 작업을 취소하며 이전 세션의 늦은 결과는 새 세션에 반영하지 않는다.

## Host 실행 환경

이 PC는 Tools/JusticeAI/local.json에 원본 폴더와 Anaconda game 환경의 Python 경로가 설정되어 있다.
API 키와 모델은 원본 AI 폴더의 .env에서 읽는다. Unity 에셋/네트워크/빌드에 API 키와 .env를 복사하지 않는다.
다른 PC가 Host가 되려면 Python 의존성을 설치하고 원본 AI 폴더와 .env를 준비한 뒤 local.example.json을 local.json으로 복사해 경로를 수정한다.
Guest에는 Python/API 키가 필요 없다. 현재 개발 빌드는 이 PC의 경로 설정 파일만 포함한다.
원본 main.py와 case_prompt.txt는 수정하지 않았으며, 원본 함수 변경은 다음 요청에 반영된다.

## 범위와 검증

현재 AI는 사건 개요(case_summary, 최대 300자)와 공통 질문(common_question, 최대 150자)만 생성한다.
판결·점수·개인 질문·STT 답변 평가·발언자 선택은 원본 AI에 없으며 자동 구현하지 않았다.
선택된 모델의 요청이 실제로 발생하며 자동 반복 호출하지 않는다.
연동 통신 버전은 v3이므로 양쪽 모두 새 빌드를 사용한다.

직접 API 테스트: 아이콘 + 리모콘으로 실제 사건/질문 JSON 응답 확인.
네트워크 smoke 테스트의 사건은 별도의 고정 fixture이며 API 품질 검증이 아니다.
