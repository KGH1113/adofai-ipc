# 시작하기

[목차](000-DevelopGuide.md) | [English](../en/001-GettingStarted.md)

ADOFAI와 UnityModManager가 필요합니다. 런타임은 netstandard2.1을 대상으로 하며 격리한
managed WebSocket 구현을 포함합니다. 게임의 Mono HttpListener WebSocket upgrade 구현은
NotImplementedException을 던지므로 사용하지 않습니다.

v1 준비 중에는 `./scripts/run.sh build`로 로컬 소스를 빌드하세요. 어셈블리와 테스트를 검증하고
설정된 `Mods/AdofaiIpc`에 설치합니다. `ADOFAIIPC_SKIP_INSTALL=1`을 지정하면 설치를 생략합니다.
`./scripts/run.sh package`는 모드 ZIP과 체크섬을 만들고 검증합니다. 공개 릴리즈는 수행하지 않습니다.

IPv4 loopback의32145부터32155까지 사용 가능한 포트를 선택합니다.
`ws://127.0.0.1:<port>/ipc/ws`와 subprotocol `adofai-ipc.v3`로 연결합니다. SDK는 직접 WebSocket
연결을 시도하고 welcome을 검증합니다. `GET /ipc/health`는 진단용으로 유지하며 server,
serverVersion, protocolVersion, port를 반환합니다.

소비 모드는 `build/AdofaiIpc/AdofaiIpc.dll`을 참조하고 Bootstrap manifest의 최소 버전을1.0.0으로
설정해 고정된 dependency 산출물을 패키징합니다. TS SDK는 [SDK 가이드](005-JsClient.md)대로
소스 snapshot을 사용합니다. 이번 준비 작업에서는 npm에 게시하지 않습니다.
