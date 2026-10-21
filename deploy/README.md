# npm and documentation deployment

`dev`에서 개발하고 `main`으로 머지합니다. `.github/workflows/release.yml`은 SDK·TypeSpec 테스트,
버전·레이어 검사, 문서 검사·빌드와 Docker smoke test가 통과한 커밋을 배포합니다.
GitHub 환경 `npm`과 `home-docs`는 `main` 브랜치만 허용합니다. PR과 `dev`에서는 공개 배포하지 않습니다.
실제 C# 상호운용 검증은 게임 DLL이 있는 로컬 환경에서 `./scripts/run.sh client`로 수행합니다.
GitHub 러너에는 게임 DLL이나 로컬 `.env`를 업로드하지 않습니다.

## npm 최초 연결

공개 대상은 `adofai-ipc`와 `@adofai-ipc/typespec`입니다. npm 계정은 두 패키지의 게시 권한과
`adofai-ipc` 조직의 권한이 있어야 합니다. 로컬 npm CLI 11.18 이상에서 `npm login` 후 실행합니다.

```bash
bash scripts/tasks/release/setup-npm.sh
```

이 스크립트는 현재 소스를 pack하고, 레지스트리와 파일이 같은 기존 버전은 건너뜁니다.
아직 없는 패키지를 최초 게시한 다음 두 패키지의 Trusted Publisher를 연결합니다.
npm이 브라우저 인증 또는 2FA를 요구하면 본인이 직접 완료합니다. 토큰을 채팅이나 Git에 저장하지 않습니다.
연결 내용은 다음과 같습니다.

| 필드 | 값 |
| --- | --- |
| Provider | GitHub Actions |
| Organization or user | `KGH1113` |
| Repository | `adofai-ipc` |
| Workflow filename | `release.yml` |
| Environment | `npm` |
| Allowed actions | `npm publish` 허용 |

이미 패키지가 있으면 npm 웹 설정에서 같은 내용을 등록해도 됩니다.
정규 배포는 GitHub OIDC를 사용하므로 `NPM_TOKEN`을 등록하지 않습니다.
참고: https://docs.npmjs.com/trusted-publishers/

## Tailscale 인증

Tailscale 관리 콘솔의 Settings → OAuth clients에서 클라이언트를 생성합니다.
`auth_keys` 쓰기 권한과 `tag:gh-runner` 태그를 허용합니다. 해당 태그에서 홈서버 `kgh`로
연결하고 `kgh` 사용자로 Tailscale SSH를 실행할 수 있어야 합니다.
기존 TUFReplay용 OAuth 클라이언트도 같은 권한을 갖고 있으면 사용할 수 있습니다.
Client ID와 Secret은 아래 대화형 입력으로 GitHub `home-docs` 환경에 직접 저장합니다.

```bash
gh secret set TS_OAUTH_CLIENT_ID --repo KGH1113/adofai-ipc --env home-docs
gh secret set TS_OAUTH_SECRET --repo KGH1113/adofai-ipc --env home-docs
```

참고: https://github.com/tailscale/github-action

## 홈서버

| 항목 | 값 |
| --- | --- |
| URL | `https://adofai-ipc.impl1113.dev/docs/` |
| 호스트 포트 | `127.0.0.1:4180` |
| Compose 프로젝트 | `adofai-ipc-docs` |
| 배포 루트 | `/home/kgh/adofai-ipc-docs` |
| 커밋별 설정 | `releases/<SHA>/` |
| 현재 설정 | `current` 심볼릭 링크 |
| 사용자 systemd | `adofai-ipc-docs.service` |

최초 라우팅은 `deploy/docs/bootstrap-routing.py`를 홈서버에 복사한 뒤 미리보기로 확인합니다.
`sudo python3 bootstrap-routing.py --apply`는 기존 설정을 백업하고 다음 작업을 수행합니다.

1. `/etc/nginx/sites-available/impl1113.dev`에 문서 hostname → `127.0.0.1:4180` 블록을 추가합니다.
2. 실제 서비스가 사용하는 `/etc/cloudflared/config.yml`에 hostname → `127.0.0.1:8080`을 추가합니다.
3. Nginx와 Tunnel 설정을 검증하고 `cloudflared tunnel route dns impl1113-home adofai-ipc.impl1113.dev`를 실행합니다.
4. `loginctl enable-linger kgh`로 부팅 시 사용자 서비스를 시작하고 Nginx reload·Tunnel restart를 수행합니다.

기존 다른 hostname과 컨테이너에는 배포 명령을 실행하지 않습니다. 최초 설정 이후 Actions는 sudo 없이 동작합니다.

## 배포와 복구

이미지는 Actions에서 `linux/amd64`로 빌드하고 `adofai-ipc-docs:<전체 SHA>`로 전송합니다.
서버에서는 빌드하지 않습니다. systemd와 Compose는 이미 전송한 이미지만 사용합니다.
문서 컨테이너는 Nginx 하나이며 HTML, 정적 자원, Pagefind 검색, sitemap과 404 페이지를 제공합니다.

새 npm 버전이 필요한 경우 `manifest.json`, 두 npm `package.json`, `Directory.Build.props`,
C# `ProductVersion`, TS `CLIENT_VERSION`을 함께 올리고 lockfile을 갱신합니다.
`./scripts/run.sh check`가 버전 일치를 검사합니다. 자동 patch 증가는 하지 않습니다.
이미 게시된 버전은 모든 파일을 비교해 같을 때만 생략합니다. 같은 버전에 내용이 바뀌면 배포가 실패합니다.
레지스트리 인증·통신 오류는 새 버전으로 취급하지 않습니다.

문서 이미지가 준비된 뒤 npm을 게시하고 홈서버 문서를 배포합니다. 두 npm 패키지는 순서대로 게시합니다.
일부만 게시된 뒤 실패하면 같은 커밋으로 재실행해 이미 게시된 패키지를 생략하고 계속합니다.
npm 게시 완료 후 문서 배포가 실패해도 npm 버전은 유지되며, 문서만 이전 설정·이미지로 복구합니다.
이미지·이전 release는 자동 삭제하지 않습니다.

재시도는 GitHub Actions에서 실패한 작업을 재실행하거나 `main`의 workflow_dispatch로 수행합니다.
새 main 커밋이 있으면 오래된 실행은 배포를 중단하므로 최신 main을 실행합니다.
문서만 수정한 main 반영도 동일한 npm 패키지를 확인한 뒤 문서를 갱신합니다.

```bash
systemctl --user status adofai-ipc-docs.service
cd /home/kgh/adofai-ipc-docs/current
docker compose --project-name adofai-ipc-docs --env-file compose.env -f compose.yml logs --tail=100
```

정상 배포는 `/docs/deployment.json`의 `gitSHA`를 서버 로컬 주소와 공개 HTTPS 주소에서 모두 확인합니다.
이 파일은 캐시하지 않습니다. `_astro`의 해시 자원만 장기 캐시하며 HTML은 재검증합니다.
