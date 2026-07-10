<div align="center">

![ADOFAI-IPC](https://capsule-render.vercel.app/api?type=waving&height=220&color=0:050505,45:101827,100:5eead4&text=ADOFAI-IPC&fontColor=e6fffb&fontAlignY=38&desc=One%20Localhost%20IPC%20Gateway%20For%20ADOFAI%20Mods&descAlignY=58&animation=fadeIn)

[![Runtime](https://img.shields.io/badge/runtime-ADOFAI%20%2F%20Unity-111827?style=for-the-badge&logo=unity&logoColor=white)](https://store.steampowered.com/app/977950/A_Dance_of_Fire_and_Ice/)
[![Mod Loader](https://img.shields.io/badge/mod%20loader-UnityModManager-7c3aed?style=for-the-badge)](https://www.nexusmods.com/site/mods/21)
[![Standalone](https://img.shields.io/badge/runtime-standalone-059669?style=for-the-badge)](#runtime)
[![Build](https://img.shields.io/badge/build-.NET%20SDK-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Target](https://img.shields.io/badge/target-netstandard2.1-2563eb?style=for-the-badge)](https://learn.microsoft.com/dotnet/standard/net-standard)
[![Docs](https://img.shields.io/badge/docs-guide-f97316?style=for-the-badge)](docs/000-DevelopGuide.md)

<br />

![Tech stack](https://skillicons.dev/icons?i=cs,dotnet,unity,bash)

**One localhost IPC gateway for ADOFAI mods**

</div>

## Overview

ADOFAI-IPC는 A Dance of Fire and Ice 모드들이 함께 사용할 수 있는
로컬 HTTP IPC 게이트웨이입니다.

각 모드가 따로 localhost 서버를 띄우는 대신, AdofaiIpc가 하나의
listener를 소유하고 다른 모드들은 자신의 namespace와 method만 등록합니다.
브라우저 스크립트, 웹 UI, CLI 같은 외부 클라이언트는 같은 IPC 프로토콜로
여러 모드의 기능을 호출할 수 있습니다.

## Features

- 하나의 localhost HTTP listener로 여러 모드 IPC 처리
- 모드별 namespace 기반 method 등록
- namespace/method discovery endpoint 제공
- Unity main thread가 필요한 handler를 위한 `RegisterMainThread` 지원
- 모드 lifecycle에 맞춘 register/unregister 흐름 지원

## Installation

1. [GitHub Releases](https://github.com/KGH1113/adofai-ipc/releases)에서 `AdofaiIpc.zip`을 다운로드합니다.
2. 압축 파일 안의 `AdofaiIpc` 폴더를 ADOFAI의 `Mods` 폴더에 넣습니다.
3. 게임을 실행한 뒤 `http://127.0.0.1:32145/ipc/health`로 동작을 확인합니다.

설치 결과는 다음 구조가 되어야 합니다.

```text
Mods/
└── AdofaiIpc/
    ├── AdofaiIpc.dll
    └── Info.json
```

## Documentation

자세한 사용법은 문서 가이드를 참고해주세요.

1. [AdofaiIpc 개발 가이드](docs/000-DevelopGuide.md)

## Runtime

ADOFAI-IPC는 별도 framework 모드 없이 UnityModManager에서 직접 실행됩니다.

Required at runtime:

- A Dance of Fire and Ice
- UnityModManager
- ADOFAI-IPC installed under the ADOFAI `Mods/AdofaiIpc` directory

## Build

Build this project with the repository build script:

```bash
./build.sh
```

Create the release archive and SHA-256 checksum with:

```bash
./package.sh
```

## Tech Stack

- **C# / .NET SDK**: mod implementation and build tooling.
- **netstandard2.1**: target framework for Unity compatibility.
- **UnityModManager**: ADOFAI mod loading.
- **Newtonsoft.Json**: IPC request and response serialization.
- **HttpListener**: localhost IPC server.
- **Bash / .env**: local build and install configuration.
