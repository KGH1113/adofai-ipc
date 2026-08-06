# AdofaiIpc 개발 가이드 - 시작하기

## [목차로 이동](000-DevelopGuide.md)

1. [AdofaiIpc란?](#1-adofaiipc%EB%9E%80)
2. [런타임 요구사항](#2-%EB%9F%B0%ED%83%80%EC%9E%84-%EC%9A%94%EA%B5%AC%EC%82%AC%ED%95%AD)
3. [포트 정책](#3-%ED%8F%AC%ED%8A%B8-%EC%A0%95%EC%B1%85)
4. [동작 확인](#4-%EB%8F%99%EC%9E%91-%ED%99%95%EC%9D%B8)

---

## 1. AdofaiIpc란?

AdofaiIpc는 ADOFAI 모드와 외부 클라이언트 사이를 연결하는 로컬 IPC 게이트웨이입니다.

브라우저 userscript, 웹 UI, CLI, 개발 도구는 AdofaiIpc의 HTTP endpoint로 요청을
보내고, AdofaiIpc는 요청의 `namespace`와 `method`를 기준으로 실제 모드 handler를
호출합니다.

모드 개발자는 직접 `HttpListener`를 만들 필요 없이 AdofaiIpc API로 method만
등록하면 됩니다.

---

## 2. 런타임 요구사항

AdofaiIpc는 별도 framework 모드 없이 UnityModManager에서 직접 실행됩니다.

필요한 항목은 다음과 같습니다.

* A Dance of Fire and Ice
* UnityModManager
* `Mods/AdofaiIpc`에 설치된 AdofaiIpc

AdofaiIpc를 사용하는 다른 모드는 자신의 프로젝트에서 `AdofaiIpc.dll`을 참조해야 합니다.
`AdofaiIpc.Bootstrap.dll`을 포함한 모드는 AdofaiIpc가 설치되어 있지 않을 때 GitHub
Releases에서 최신 패키지를 자동으로 설치할 수도 있습니다.

---

## 3. 포트 정책

AdofaiIpc는 기본적으로 다음 주소에서 시작을 시도합니다.

```text
http://127.0.0.1:32145/
```

이미 해당 포트가 사용 중이면 `32146`, `32147`처럼 1씩 증가시키며 사용 가능한 포트를
찾습니다. 현재 구현은 `32155`까지 fallback을 시도합니다.

클라이언트는 고정 포트 하나만 가정하기보다 `32145`부터 순서대로 `/ipc/health`를
호출해 살아있는 AdofaiIpc 서버를 찾는 방식을 권장합니다.

---

## 4. 동작 확인

ADOFAI가 실행 중이고 AdofaiIpc가 정상적으로 로드되었다면 health endpoint로 확인할 수 있습니다.

```bash
curl -s http://127.0.0.1:32145/ipc/health
```

fallback 포트로 실행된 경우에는 다음처럼 포트를 바꿔 확인합니다.

```bash
curl -s http://127.0.0.1:32146/ipc/health
```

응답에 `ok`, `server`, `protocolVersion`, `port` 같은 정보가 포함되면 IPC listener가
정상적으로 동작 중입니다.
