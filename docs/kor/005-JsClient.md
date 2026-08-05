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

const client = await tryConnect();
```

각 포트의 연결 실패는 탐색 과정에서 무시됩니다. 모든 후보 포트가 실패하면
`tryConnect`는 code가 `UNAVAILABLE`인 `IpcConnectionError`를 던집니다.

---

## 3. Namespace 호출

직접 `namespace`와 `method`를 넘겨 호출할 수 있습니다.

```ts
const result = await client.call({
  namespace: "tufhelper2",
  method: "level.open-from-id",
  params: {
    id: "1234"
  }
});
```

namespace client를 만들어서 사용할 수도 있습니다.

```ts
const tufhelper = client.namespace("tufhelper2");

await tufhelper.call("level.open-from-id", {
  id: "1234"
});
```

연결 실패는 `IpcConnectionError`로 전달됩니다. 설정한 제한 시간을 넘긴 요청은
`IpcConnectionError`를 상속하고 code가 `TIMEOUT`인 `IpcTimeoutError`를 던집니다.
연결 불가와 timeout을 같은 방식으로 처리하려면 `isIpcUnavailable`을 사용할 수 있습니다.

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
