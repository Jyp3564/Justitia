# 한국어 STT 테스트

1. Steam에 로그인하고 게임에서 세션 만들기를 누른다. 혼자 Host로도 테스트 가능하다.
2. 대기 로비 또는 게임 맵에서 V 키를 누르고 한국어로 말한 뒤 놓는다. 최대 30초씩 발언한다.
3. Unity Console에 `[STT/ko] 인식된 문장`이 출력되고 게임 화면 상단에도 결과가 나타난다.
4. 빌드에서는 화면과 Unity Player.log에서 결과를 확인한다.

Esc → 음성 설정에서 마이크가 켜져 있는지 확인한다. 설정·친구 목록, 주제 입력, Steam 오버레이 또는 다른 창에 포커스가 있을 때는 발언을 시작하지 않는다.
마이크 장치는 Steam 음성 설정에서 선택한다. STT는 별도 마이크 캡처 대신 Steam이 캡처한 동일 음성을 16kHz 모노 PCM으로 변환해 사용한다.
마이크 음소거, 발언 권한 회수, 세션 종료는 진행 중인 발언을 취소한다. V를 정상적으로 놓으면 Steam의 짧은 잔여 음성까지 수집해 문장 끝을 처리한다. 키를 놓은 뒤 잔여 음성은 상대에게 송신하지 않는다.

로컬 whisper.cpp v1.9.2 + 다국어 small-q5_1 모델, 언어 ko 고정. 영어 번역을 요청하지 않는다.
오디오가 외부 STT 서비스로 업로드되지 않는다. 변환용 임시 WAV/텍스트 파일은 작업 종료 시 제거한다. 인식 결과는 테스트를 위해 Console/Player.log에 남는다.
최대 1개 작업을 실행한다. 인식 중 다시 말하면 음성 대화는 가능하지만 해당 발언의 STT는 생략된다고 표시한다. 긴 발언이나 낮은 사양에서는 결과가 늦을 수 있다.
무음과 아주 짧은 입력은 건너뛴다. 실제 정확도는 발음·잡음·장치에 따라 달라지며, 확정 판정 데이터로 자동 사용하지 않는다.

## 설치 및 빌드

현재 프로젝트에는 실행 파일과 약 190MB 모델이 설치되어 있다.
새 체크아웃에서는 `powershell -ExecutionPolicy Bypass -File Tools/setup-stt.ps1` 실행 후 Unity 빌드를 한다.
실행 파일, DLL, 모델, 라이선스는 Windows 빌드의 `Tools/Whisper`에 자동 복사된다. 게임 폴더 전체를 전달해야 한다.
모델 SHA256: AE85E4A935D7A567BD102FE55AFC16BB595BDB618E11B2FC7591BC08120411BB

## 이후 발언자 선택 연결

`LobbyPanel.Voice.SetSpeakingAllowed(false)`로 발언을 차단하고 선택된 플레이어에게만 true를 적용한다.
현재 기본값은 테스트용 true다. 이 API는 로컬 입력 제한 연결점이며, 정의의 여신 판정/Host의 발언권 동기화와 수신측 권한 검증은 아직 구현하지 않았다.
STT 결과 후속 처리는 `LocalSpeechToText.Transcribed` 이벤트에 연결할 수 있다. 현재 결과는 로컬 테스트 출력이며 상대에게 전송하지 않는다.

검증용 `TestResults/STT/korean-synthetic.wav`는 Microsoft Heami로 로컬 생성한 한국어 합성 음성이다. 실제 사용자 마이크 녹음이 아니다.
참고: https://github.com/ggml-org/whisper.cpp/tree/v1.9.2
