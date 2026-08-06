# AdofaiIpc 개발 가이드 - JavaScript/TypeScript Client

## [목차로 이동](000-DevelopGuide.md)

1. [패키지 위치](#1-%ED%8C%A8%ED%82%A4%EC%A7%80-%EC%9C%84%EC%B9%98)
2. [포트 자동 탐색](#2-%ED%8F%AC%ED%8A%B8-%EC%9E%90%EB%8F%99-%ED%83%90%EC%83%89)
3. [Namespace 호출](#3-namespace-%ED%98%B8%EC%B6%9C)
4. [빌드](#4-%EB%B9%8C%EB%93%9C)

---

## 1. 패키지 위치

JavaScript/TypeScript client는 monorepo 안의 `packages/client`에 있습니다.

```text
packages/client
```

패키지 이름은 `@adofai-ipc/client`입니다.

---

## 2. 포트 자동 탐색

AdofaiIpc는 기본 포트 `32145`에서 시작하고, 포트가 사용 중이면 `32155`까지 fallback을
시도합니다.

client에서는 `tryConnect`로 실행 중인 AdofaiIpc listener를 찾을 수 있습니다.

```ts
import { tryConnect } from "@adofai-ipc/client";

const client = await tryConnect({
  probeTimeoutMs: 500,
  requestTimeoutMs: 10_000
});
```

각 포트의 연결 실패는 탐색 과정에서 무시됩니다. 모든 후보 포트가 실패하면
`tryConnect`는 code가 `UNAVAILABLE`인 `IpcConnectionError`를 던집니다.

`tryConnect` 성공은 AdofaiIpc listener가 실행 중이라는 것만 보장합니다. 대상 namespace가
등록되었거나 해당 모드의 초기화가 끝났다는 의미는 아닙니다. 기존 `timeoutMs` option은
두 timeout을 함께 설정하는 deprecated alias로 유지됩니다.

---

## 3. Namespace 호출

직접 `namespace`와 `method`를 넘겨 호출할 수 있습니다.

```ts
const result = await client.call({
  namespace: "tufhelper2",
  method: "level.open-from-id",
  params: {
    id: "1234"
  },
  timeoutMs: 30_000
});
```

namespace client를 만들어서 사용할 수도 있습니다.

```ts
const tufhelper = client.namespace("tufhelper2");

await tufhelper.call("level.open-from-id", {
  id: "1234"
});
```

namespace가 아직 등록 중일 수 있으면 listener 탐색과 별도로 기다릴 수 있습니다.

```ts
await client.waitForNamespace("tufhelper2", {
  status: "ready",
  timeoutMs: 15_000,
  pollIntervalMs: 100
});
```

namespace는 등록 직후 `initializing` 상태가 됩니다. namespace를 소유한 모드가 명시적으로
`ready` 또는 `error`로 전환해야 하며, AdofaiIpc는 `ready`가 되기 전까지 method 호출을
차단합니다. ready 대기 만료는 `namespace_initializing`, 초기화 실패는 `namespace_error`로
구분됩니다.

연결 실패는 `IpcConnectionError`로 전달됩니다. 설정한 제한 시간을 넘긴 요청은
`IpcConnectionError`를 상속하고 code가 `TIMEOUT`인 `IpcTimeoutError`를 던집니다.
`namespace_not_found`, `namespace_initializing`, `namespace_error`를 포함한 protocol 오류는
`IpcResponseError`로 전달됩니다.
`isIpcUnavailable`은 code가 `UNAVAILABLE`인 연결 오류만 판별하며 timeout은 포함하지 않습니다.

```ts
import {
  IpcTimeoutError,
  isIpcUnavailable
} from "@adofai-ipc/client";

try {
  await client.health();
} catch (error) {
  if (error instanceof IpcTimeoutError) {
    console.warn(`AdofaiIpc 요청이 ${error.timeoutMs}ms 후 timeout되었습니다.`);
  } else if (isIpcUnavailable(error)) {
    console.warn("AdofaiIpc에 연결할 수 없습니다.");
  }
}
```

---

## 4. 빌드

root workspace에서 client 패키지만 빌드합니다.

```bash
pnpm --filter @adofai-ipc/client build
```

전체 TypeScript workspace를 확인하려면 다음 명령을 사용합니다.

```bash
pnpm check
```
