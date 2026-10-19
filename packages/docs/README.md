# ADOFAI IPC 개발 문서

Astro Starlight 기반 한국어 개발 문서입니다. TypeSpec 계약, C#·TypeScript API, 양방향 호출과 저장소 구성을 다룹니다.

## 로컬 실행

Node.js 22.22.0 이상과 저장소에 지정된 pnpm을 사용합니다. 저장소 루트에서 실행합니다.

```bash
pnpm install
pnpm docs:dev
```

개발 서버의 기본 포트는 4321입니다. 터미널에 출력된 주소로 접속합니다.

## 검사와 빌드

```bash
pnpm docs:check
pnpm docs:build
pnpm docs:preview
```

정적 결과는 `packages/docs/dist/`에 생성됩니다. 위 명령은 문서 사이트를 대상으로 합니다. C# 프로젝트의 빌드는 저장소 루트의 `./scripts/run.sh build`를 사용합니다.

배포 시 `astro.config.mjs`의 `site`에 실제 사이트 주소를 지정합니다. `site`가 없으면 sitemap 생성을 건너뜁니다.

## 파일 구성

| 경로 | 용도 |
| --- | --- |
| `src/content/docs/learn/contract.mdx` | TypeSpec 계약 정의 |
| `src/content/docs/reference/` | 모드·앱 API와 동작 규칙 |
| `src/content/docs/guides/monorepo.mdx` | 코드 생성, 모노레포·독립 저장소 구성 |
| `src/content/docs/guides/settings-example.mdx` | 예제 프로젝트 개요 |
| `src/content/docs/examples/settings/` | 준비·등록·조회·저장의 단계별 실습 |
| `src/content/i18n/ko.json` | 한국어 UI 문구 |
| `astro.config.mjs` | 탐색 메뉴와 Starlight 설정 |
| `src/styles/custom.css` | 문서 스타일 |

## 편집 기준

- 기능 설명 뒤 바로 코드를 제시하고, 필수 인자·반환값·조건만 설명합니다.
- 같은 내용은 한 곳에서만 설명합니다. 등록 코드와 전체 예제를 매 절마다 반복하지 않습니다.
- 구현 진행 상황과 내부 개발 기록은 공개 API 문서에 포함하지 않습니다.
- API 참조의 예제는 해당 API만 설명하며, 이전 절의 상태나 실제 사용자 프로젝트에 의존하지 않습니다.
- 예제 프로젝트 실습은 단계별로 누적합니다. 각 단계의 목표, 코드 변경분과 확인할 결과를 설명하며 API 참조와 구분합니다.
- 생성 명령과 오류 타입 등 정의하지 않은 API를 임의로 추가하지 않습니다.
