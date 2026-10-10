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

ADOFAI-IPC 2.0.0은 ADOFAI 모드와 외부 앱을 연결하는 양방향 WebSocket 런타임입니다.
각 모드는 DLL을 동봉하고 기능을 등록합니다. 서버는 게임 프로세스에 속하며 최초 호출 모드의
비활성화와 무관하게 유지합니다. wire protocol은 3, 공용 C# 계약 major는 1입니다.

## Architecture

소비 모드는 Contracts와 Loader만 참조하고 런타임 구현을 직접 참조하지 않습니다.
로더가 설치된 모든 `Mods/<mod>/ipc/manifest.json` 후보에서 호환되는 최신 안정 런타임을 선택합니다.
선택은 첫 시작 전에 끝내며 게임 종료까지 고정합니다. 늦게 켠 비호환 모드는 자신의 등록만 실패합니다.

- 게임 프로세스 공용 호스트, WebSocket 서버 하나, Unity dispatcher 하나
- 모드별 기능 등록·해제, 초기 snapshot과 상태 변경 이벤트
- 양방향 명령, 이름 있는 결과, correlationId와 취소 token
- 연결 종료·구독 해제·기능 해제에 따른 명령 취소와 다운로드 티켓 폐기
- 같은 프로세스의 local peer도 공통 router 사용
- IPv4 loopback 32145..32155, `ws://127.0.0.1:<port>/ipc/ws`, `adofai-ipc.v3`
- 제어 메시지 2 MiB, 제한된 peer·명령·송신 큐, heartbeat
- 파일은 1회용 HTTP 티켓과 64 KiB chunk 스트림
- 게임 스레드 대기 큐 1024개, 프레임당 최대 64개 실행

별도 공용 라이브러리 모드, 자동설치, 의존 모드 활성화, 자체 업데이트, launcher, shim,
migration 코드는 제거했습니다. `IFeatureRegistration.Dispose()`는 해당 기능만 해제합니다.
호스트는 Unity application quitting/unloading에서 종료합니다.

## Build and package

```bash
./scripts/run.sh check
./scripts/run.sh build                  # DLL 빌드 + C# 테스트; 게임에 설치하지 않음
./scripts/run.sh client                 # TypeScript 빌드·타입 검사·테스트
./scripts/run.sh package                # 동봉 DLL ZIP + checksum 검증
./scripts/run.sh client-sync /path/to/consumer/vendor/adofai-ipc
```

기본 빌드 출력은 `build/ipc`, ZIP은 `build/AdofaiIpc-v2.zip`입니다.
로컬 `.env`의 출력 경로 설정은 유지됩니다. 공개 릴리즈나 npm 게시를 수행하지 않습니다.
ZIP을 소비 모드 폴더에 펼치면 다음 파일을 동봉하게 됩니다.

```text
Mods/ExampleMod/
  ExampleMod.dll
  Info.json                            # 소비 모드 자신의 진입점
  AdofaiIpc.Contracts.dll
  AdofaiIpc.Loader.dll
  ipc/
    manifest.json
    AdofaiIpc.Runtime.dll
```

Contracts와 Loader의 AssemblyVersion은 계약 major 1에서 `1.0.0.0`으로 유지합니다.
제품 버전·계약 major·wire major·기능별 메시지 major는 별도로 관리합니다.
새 DLL은 게임 재시작 후 적용하며 실행 중 교체하지 않습니다.

## Documentation

- [한국어 개발 가이드](docs/kor/000-DevelopGuide.md)
- [English developer guide](docs/en/000-DevelopGuide.md)
- [동봉 런타임과 버전 계약](docs/kor/006-BundledRuntime.md)
- [v2 구현과 검증](docs/releases/v2.0.0.md)

## Runtime and validation

ADOFAI의 Unity/Mono와 netstandard2.1을 대상으로 합니다. 게임의 Newtonsoft.Json을 사용하며
managed WebSocket 소스를 런타임에 포함합니다. UnityModManager와 JALib에 의존하지 않습니다.

자동 테스트는 .NET 8에서 버전 선택·공용 런타임 수명과 실제 WebSocket 통신을 검증합니다.
실제 게임에서의 계약 DLL 통합 로딩과 Unity 종료 훅은 두 소비 모드로 추가 검증해야 합니다.
