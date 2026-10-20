<div align="center">

![ADOFAI-IPC](https://capsule-render.vercel.app/api?type=waving&height=220&color=0:050505,45:101827,100:5eead4&text=ADOFAI-IPC&fontColor=e6fffb&fontAlignY=38&desc=One%20Localhost%20IPC%20Gateway%20For%20ADOFAI%20Mods&descAlignY=58&animation=fadeIn)

[![Runtime](https://img.shields.io/badge/runtime-ADOFAI%20%2F%20Unity-111827?style=for-the-badge&logo=unity&logoColor=white)](https://store.steampowered.com/app/977950/A_Dance_of_Fire_and_Ice/)
[![Mod Loader](https://img.shields.io/badge/mod%20loader-UnityModManager-7c3aed?style=for-the-badge)](https://www.nexusmods.com/site/mods/21)
[![Standalone](https://img.shields.io/badge/runtime-standalone-059669?style=for-the-badge)](#build-and-package)
[![Build](https://img.shields.io/badge/build-.NET%20SDK-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Target](https://img.shields.io/badge/target-netstandard2.1-2563eb?style=for-the-badge)](https://learn.microsoft.com/dotnet/standard/net-standard)
[![Docs](https://img.shields.io/badge/docs-guide-f97316?style=for-the-badge)](docs/README.md)

<br />

![Tech stack](https://skillicons.dev/icons?i=cs,dotnet,unity,bash)

**One localhost IPC gateway for ADOFAI mods**

</div>

## Overview

ADOFAI-IPC는 A Dance of Fire and Ice 모드와 같은 컴퓨터에서 실행하는 외부 앱을 연결하는
라이브러리입니다. 웹 UI, CLI, 개발 도구에서 모드에 작업을 요청하거나 게임 상태를 받을 때 사용합니다.
IPC는 서로 다른 프로그램이 정보를 주고받는다는 뜻입니다.

TypeSpec 계약에서 C#·TypeScript API를 생성합니다. 모드는 `RegisterMod()`와 `Handle()`로
메서드를 구현하고, 앱은 `registerApp()`과 `use()`로 얻은 채널에서 호출합니다.
모드는 등록된 앱을 선택해 먼저 호출하거나 요청한 앱에 응답할 수 있습니다. SDK가 연결·재접속·직렬화와 입력·결과 검증을 처리합니다.

제품 버전은 **2.0.0**입니다. v1은 별도로 릴리즈하지 않으며 v0에서 바로 v2로 전환합니다.

## Architecture

각 모드는 필요한 DLL을 자신의 패키지에 포함합니다. 모드에 동봉한 로더가 호환되는 최신 안정
런타임을 선택하고, 게임 안에서 공용 서버를 시작합니다. 모드를 끄면 해당 기능만 해제하고
다른 모드의 통신은 유지합니다. 서버는 게임 종료 때 함께 종료됩니다.

- 외부 앱의 명령 요청과 모드의 결과·상태 변경 알림
- 모드에서 외부 앱으로 보내는 명령과 같은 프로세스 안의 클라이언트
- 연결 종료·기능 해제에 따른 명령 취소
- `ipc.File` 반환·앱 알림을 통한 파일 스트리밍 전송
- loopback 전용 연결, 메시지·작업 큐 제한, 연결 생존 확인

계약 정의와 API는 [Starlight 문서](docs/README.md)에 있습니다. 모드·앱·계약은 같은 모노레포에서 관리하는 것을 권장합니다.
독립 저장소에서는 모드가 계약 원본을 관리하고 웹 저장소에 TS 생성 패키지와 SDK를 고정해 사용합니다.

## Repository structure

```text
docs/                           # Starlight 개발 문서
packages/adofai-ipc/             # 통합 TypeScript SDK
packages/typespec/               # TypeScript로 작성한 TypeSpec 생성기
AdofaiIpc/src/                   # Contracts · Loader · Runtime
AdofaiIpc/tests/                 # C# 테스트
tests/interoperability/          # TS SDK와 실제 C# Runtime DLL의 통합 테스트
scripts/                        # 빌드 · 검증 · vendor 패키지 생성
```

SDK와 C# 구현은 공개 API, application, models, ports, adapters, composition으로 나눕니다.
application은 ports를 통해 전송·직렬화·런타임 로딩을 사용하고, composition에서 실제 구현을 주입합니다.
TypeSpec 생성기는 계약 해석, 공통 계약 모델, 언어별 emitter와 파일 저장을 분리합니다.
`./scripts/run.sh check`는 내부 레이어가 adapter 구현을 직접 참조하지 않는지도 검사합니다.

## Generate APIs

JavaScript SDK는 npm에서 설치합니다. 소비 앱의 `package-lock.json` 또는 동등한 lockfile에
배포 tarball의 버전과 무결성을 고정합니다.

```bash
npm install --save-exact adofai-ipc@2.1.0
```

TypeSpec 계약에서 양쪽 API를 생성합니다. 문자열·불리언·`int32`·`float64`·`safeint`, 배열, nullable,
optional, 문자열 enum/literal, `void` 반환과 모드→앱 방향의 `ipc.File`을 지원합니다. C#의 optional 필드는 `IpcOptional<T>`로
생략과 명시적인 `null`을 구분하고, 문자열 enum은 TS 값 상수도 생성합니다.
파일은 C# `IpcFile.FromPath()`·`FromStream()`·`FromWriter()`로 보내고, TS `ReceivedFile`에서
`stream()`·`text()`·`arrayBuffer()`·`blob()`으로 한 번 읽습니다. SDK가 전송과 세션 종료 시 정리를 처리합니다.
큰 파일을 저장할 때는 `await file.download()`로 브라우저 기본 다운로드에 전달합니다.
전체 본문을 JavaScript 메모리에 올리지 않으며, 저장 위치와 진행 상태는 브라우저가 관리합니다.
이 호출은 다운로드 요청을 전달하면 완료되고, 디스크 저장 완료를 기다리지는 않습니다.

```typespec
import "@adofai-ipc/typespec";

@ipc.mod("echo")
namespace Echo {
  op echo(text: string): string;
}
```

소비 프로젝트에서 출력 경로를 `adofai-ipc.config.json`에 지정하고 실행합니다.

```bash
pnpm exec adofai-ipc generate
pnpm exec adofai-ipc generate --check
```

생성된 C# 소스는 모드와 함께 컴파일하고 TS 패키지는 앱의 의존성으로 참조합니다. 모드는
`Apps`·`TryGetApp()`·`AppConnected`로 앱을 선택하고, 앱은 `onStatus()`에서 재등록 완료 후 snapshot을 조회합니다.

## Build and package

```bash
./scripts/run.sh check
./scripts/run.sh build                  # DLL 빌드 + C# 테스트; 게임에 설치하지 않음
./scripts/run.sh client                 # TS SDK·생성기·C# 통합 테스트
./scripts/run.sh package                # 동봉 DLL ZIP + checksum 검증
./scripts/run.sh sdk-sync /path/to/consumer/vendor/adofai-ipc # 전송 계층을 포함한 typed SDK
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

[Starlight 개발 문서](docs/README.md)는 TypeSpec 계약, C#·TypeScript API, 코드 생성과
저장소 구성을 다룹니다. Node.js 22.22 이상과 저장소에 지정된 pnpm으로 `pnpm install` 후
`pnpm docs:dev`를 실행합니다. `.env`의 게임·.NET 경로는 기존 빌드 설정을 따릅니다.

## Runtime and validation

ADOFAI의 Unity/Mono와 netstandard2.1을 대상으로 합니다. 게임의 Newtonsoft.Json을 사용하며
managed WebSocket 소스를 런타임에 포함합니다. UnityModManager와 JALib에 의존하지 않습니다.

자동 테스트는 .NET 8에서 버전 선택·공용 런타임 수명, 생성 타입, 양방향 호출과 실제 WebSocket 통신을 검증합니다.
