<div align="center">

![ADOFAI-IPC](https://capsule-render.vercel.app/api?type=waving&height=220&color=0:050505,45:101827,100:5eead4&text=ADOFAI-IPC&fontColor=e6fffb&fontAlignY=38&desc=One%20Localhost%20IPC%20Gateway%20For%20ADOFAI%20Mods&descAlignY=58&animation=fadeIn)

[![Runtime](https://img.shields.io/badge/runtime-ADOFAI%20%2F%20Unity-111827?style=for-the-badge&logo=unity&logoColor=white)](https://store.steampowered.com/app/977950/A_Dance_of_Fire_and_Ice/)
[![Mod Loader](https://img.shields.io/badge/mod%20loader-UnityModManager-7c3aed?style=for-the-badge)](https://www.nexusmods.com/site/mods/21)
[![Standalone](https://img.shields.io/badge/runtime-standalone-059669?style=for-the-badge)](#runtime)
[![Build](https://img.shields.io/badge/build-.NET%20SDK-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Target](https://img.shields.io/badge/target-netstandard2.1-2563eb?style=for-the-badge)](https://learn.microsoft.com/dotnet/standard/net-standard)
[![Docs](https://img.shields.io/badge/docs-guide-f97316?style=for-the-badge)](docs/README.md)

<br />

![Tech stack](https://skillicons.dev/icons?i=cs,dotnet,unity,bash)

**One localhost IPC gateway for ADOFAI mods**

</div>

## Overview

ADOFAI-IPC1.0.0은 ADOFAI 모드와 외부 앱이 함께 사용하는 양방향 WebSocket 게이트웨이입니다.
각 모드는 namespace를 등록하고 양쪽 peer가 명령·이름 있는 결과·snapshot·상태 변경 이벤트를
서로 보냅니다. wire protocol은3이며 기존 HTTP RPC API는 제거했습니다.

## Architecture

한 UnityModManager 모드가 IPv4 loopback listener를 소유합니다. 기본32145부터32155까지
선택하고 `ws://127.0.0.1:<port>/ipc/ws`, subprotocol `adofai-ipc.v3`를 사용합니다.
서버 라우터는 peer·namespace·메시지 수명을, 소비 앱은 도메인 작업·revision·소유권을 책임집니다.
Unity 작업은 main-thread command나 주입한 dispatcher에서 수행합니다.

SDK/Unity/native 구현은 composition root에서 자체 port에 주입합니다. application/domain에서
구체적인 transport나 UI framework에 의존하지 않도록 경계를 유지하세요.

- namespace lifecycle과 초기 snapshot/변경 이벤트 push
- 양방향 명령과 correlationId를 가진 이름 있는 결과 이벤트
- 연결 종료·namespace 해제 시 명령 취소, 자동 재구독과 snapshot 복구
- 같은 게임 프로세스의 모드를 위한 공통 local-peer router
- 메시지2MiB, 대기 큐·명령·peer 제한과 heartbeat
- 큰 파일은 이름 있는 결과·작은 메타데이터와1회용 HTTP 티켓으로64KiB chunk 스트리밍
- 송신 제어 메시지 크기 초과는 해당 명령만 실패시키고 연결·구독 유지
- 기존 fixed dependency shim과 versioned bootstrap/update 설치 구조 유지

## Build and install

v1 준비 중에는 로컬 소스로 작업합니다. 공개 릴리즈와 npm SDK 게시를 아직 수행하지 않습니다.

```bash
./scripts/run.sh check
./scripts/run.sh build                  # test + install into Mods/AdofaiIpc
./scripts/run.sh client                 # build/typecheck/test; no npm publish
./scripts/run.sh package                # mod ZIP + checksum validation
./scripts/run.sh client-sync /path/to/consumer/vendor/adofai-ipc
```

설치 없이 검증하려면 `ADOFAIIPC_SKIP_INSTALL=1 ./scripts/run.sh build`를 사용합니다.
설치 후 게임을 완전히 재시작하면 versioned runtime1.0.0을 사용합니다.
`GET /ipc/health`는 진단용 HTTP endpoint입니다.

```text
Mods/AdofaiIpc/
  Info.json
  AdofaiIpc.Shim.dll
  AdofaiIpc.dll
  Update/state.json
  Launcher/versions/1.0.0/AdofaiIpc.Launcher.dll
  Runtime/versions/1.0.0/AdofaiIpc.dll
```

## Documentation

- [한국어 개발 가이드](docs/kor/000-DevelopGuide.md)
- [English developer guide](docs/en/000-DevelopGuide.md)
- [v1 preparation notes](docs/releases/v1.0.0.md)
- [Dependency bootstrap](docs/en/006-DependencyBootstrap.md)

## Runtime

ADOFAI, UnityModManager와 netstandard2.1이 필요합니다. Newtonsoft.Json을 사용하며 pinned
managed WebSocket 소스는 namespace를 격리해 런타임 DLL에 포함합니다. 게임의 미구현
HttpListener WebSocket API와 별도 WebSocketSharp DLL에 의존하지 않습니다.
