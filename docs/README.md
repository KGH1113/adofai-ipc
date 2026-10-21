# ADOFAI IPC 개발 문서

Astro Starlight 기반 한국어·영어·중국어 간체 개발 문서입니다. TypeSpec 계약, C#·TypeScript API, 양방향 호출과 저장소 구성을 다룹니다.

## 언어와 번역

| 언어 | 문서 경로 | 콘텐츠 폴더 | UI 번역 |
| --- | --- | --- | --- |
| 한국어 (기본) | `/docs/` | `src/content/docs/` | `src/content/i18n/ko.json` |
| English | `/docs/en/` | `src/content/docs/en/` | `src/content/i18n/en.json` |
| 简体中文 | `/docs/zh-cn/` | `src/content/docs/zh-cn/` | `src/content/i18n/zh-CN.json` |

언어 전환은 같은 문서의 번역으로 이동합니다. 사이드바는 `astro.config.mjs`의 `translations`,
목차·테마 선택 등 기본 UI는 Starlight의 내장 번역을 사용합니다. 검색 색인은 언어별로 생성합니다.
한국어 파일을 기준으로 동일한 상대 경로의 영어·중국어 파일을 함께 관리합니다.
문서 링크는 해당 언어 경로를 사용하고, 제목을 번역하면 링크의 앵커도 함께 갱신합니다.
예제의 API·계약 식별자는 유지하며 표시 문구와 주석을 번역합니다.

`pnpm docs:build`는 번역 누락, 언어 메타데이터, 언어 간 문서 링크, 앵커·정적 자원,
검색 UI의 치환 변수와 언어별 색인을 검사합니다. 404 페이지는 `src/components/NotFound.astro`에서
언어별 문구를 관리하고 `deploy/docs/nginx.conf`에서 각 언어의 404 경로로 연결합니다.

## 로컬 실행

Node.js 22.22.0 이상과 저장소에 지정된 pnpm을 사용합니다. 저장소 루트에서 실행합니다.

```bash
pnpm install
pnpm docs:dev
```

개발 서버의 기본 포트는 4321이며 문서 경로는 `/docs/`입니다. 터미널에 출력된 주소로 접속합니다.

## 검사와 빌드

```bash
pnpm docs:check
pnpm docs:build
pnpm docs:preview
```

정적 결과는 `docs/dist/`에 생성됩니다. 위 명령은 문서 사이트를 대상으로 합니다. C# 프로젝트의 빌드는 저장소 루트의 `./scripts/run.sh build`를 사용합니다.

공개 주소는 https://adofai-ipc.impl1113.dev/docs/입니다. `astro.config.mjs`의 `site`와 `base`에
이 주소를 설정합니다. 문서 내부의 절대 링크도 `/docs/`로 시작해야 합니다.
`main` 반영 시 GitHub Actions가 npm 패키지와 문서를 배포합니다. 최초 계정 연결과 홈서버 설정은
[배포 안내](../deploy/README.md)를 참고하십시오.

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
