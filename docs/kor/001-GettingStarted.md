# 시작하기

[목차](000-DevelopGuide.md) | [English](../en/001-GettingStarted.md)

`./scripts/run.sh build`는 DLL 빌드와 테스트만 수행합니다. 게임에 자동설치하지 않습니다.
`./scripts/run.sh package`는 동봉용 ZIP과 SHA-256 체크섬을 만듭니다.
런타임은 netstandard2.1과 자체 managed WebSocket 전송 구현을 사용합니다.

소비 모드는 AdofaiIpc.Contracts.dll과 AdofaiIpc.Loader.dll만 참조합니다.
AdofaiIpc.Runtime.dll을 직접 참조하면 선택 전에 로드될 수 있으므로 참조하지 않습니다.
ZIP 내용을 자신의 모드 폴더에 넣습니다.

```text
Mods/ExampleMod/
  Info.json                       # 소비 모드 자신의 진입점
  ExampleMod.dll
  AdofaiIpc.Contracts.dll
  AdofaiIpc.Loader.dll
  ipc/
    manifest.json
    AdofaiIpc.Runtime.dll
```

계약·로더 DLL은 모드 어셈블리 옆에 둬서 참조를 해석할 수 있게 합니다.
Newtonsoft.Json은 게임의 것을 사용하고 별도 사본은 동봉하지 않습니다.
런타임은 게임의 UnityEngine.CoreModule과 Newtonsoft.Json만 사용합니다.
별도 Mods/AdofaiIpc 설치는 필요하지 않습니다.

Unity 게임 스레드에서 IpcLoader.GetRuntime(modsDirectory, requirements)를 호출합니다.
modsDirectory는 각 모드 디렉터리가 들어 있는 Mods 폴더입니다.
기능을 등록하고 도메인 상태를 준비한 뒤 MarkReady합니다.

서버는 IPv4 loopback의 32145..32155에서 포트를 선택합니다.
`ws://127.0.0.1:<port>/ipc/ws`, subprotocol `adofai-ipc.v3`를 사용합니다.
`GET /ipc/health`는 server, serverVersion, protocolVersion, port를 반환합니다.
[버전 선택](006-BundledRuntime.md)과 [모드 API](002-ModApi.md)를 참고하세요.
