# SDK 자동 전환 수동 E2E

현재 정상 전환은 [한 세트 확인 안내](migration-smoke.md)를 따른다.
아래는 최초 0.4.2 게시 당시의 확장 시나리오 기록이며, 0.4.3에서는 Mono 로딩 문제를 수정했다.
새 생성기의 기본 IPC 대상은 0.4.3이고 `--smoke`로 정상 전환 하나만 준비할 수 있다.

이 문서는 공개된 IPC `0.4.1 → 0.4.2`, Helper `0.1.9 → 1.0.0`, Replay `0.2.0-beta.4 → 1.0.0`의 실제 설치 전환을 확인하기 위한 절차다. 생성기는 **별도 테스트 폴더만** 만들며 실행 중인 게임 설치나 원래 Mods를 교체하지 않는다. 게임에서의 동작 판정은 아래 실행 절차를 수행한 뒤 기록한다.

2026-10-21에 다음 실제 공개 릴리스에서 자료를 받아 **27개 세트**를 생성했다. 공개 파일 검증과 폴더 준비는 완료했으며, Unity 게임의 첫 실행/재실행은 아직 수행하지 않았다.

| 제품 | 실제 구버전 ZIP 출처 | 이번 게시 버전 |
| --- | --- | --- |
| IPC | [v0.4.1](https://github.com/KGH1113/adofai-ipc/releases/tag/v0.4.1) | [v0.4.2](https://github.com/KGH1113/adofai-ipc/releases/tag/v0.4.2) |
| Helper | [v0.1.9](https://github.com/KGH1113/TUFHelperLite/releases/tag/v0.1.9) | [v1.0.0](https://github.com/KGH1113/TUFHelperLite/releases/tag/v1.0.0) |
| Replay | [v0.2.0-beta.4](https://github.com/KGH1113/TUFReplay/releases/tag/v0.2.0-beta.4) | [v1.0.0](https://github.com/KGH1113/TUFReplay/releases/tag/v1.0.0) |

## 준비된 자료와 재생성

기본 위치는 `/Users/kgh/dev/src/adofai-ipc/build/migration-e2e`다.

| 경로 | 내용 |
| --- | --- |
| `cases/<번호-이름>/Mods` | 게임을 종료한 뒤 직접 교체할 완성된 모드 폴더 세트 |
| `cases/<번호-이름>/fixture.json` | 설치 조합, 네트워크 조건, ZIP 추출 뒤 적용한 수정, 기대 결과 |
| `cases/<번호-이름>/preservation.json` | 보존 표본 SHA-256 및 확인할 설정 값 |
| `downloads/<제품-old 또는 new>` | 실제 공개 ZIP, 체크섬/매니페스트, GitHub 릴리스 원본 메타데이터 |
| `provenance.json` | 태그, 공개 시각, 다운로드 URL, asset ID, 크기, SHA-256, GitHub digest |
| `report.json` | 준비 완료 사례와 자동 확인 항목. `unityGameE2e: false`는 게임 미실행을 뜻함 |
| `fault-inputs` | 공개 신버전 ZIP에서 만든 잘린 ZIP과 필수 본체 누락 ZIP. 업데이터 테스트용이며 정상 설치용이 아님 |

재생성은 저장소 어디에서든 아래처럼 실행한다. Python 3.9 이상, `rtk`, 로그인된 `gh`, GitHub 접근이 필요하다. 이미 생성된 테스트 복사본은 `--replace`로 초기화한다. 게임에 복사해 둔 폴더에는 영향을 주지 않는다.

```bash
rtk proxy python3 /Users/kgh/dev/src/adofai-ipc/scripts/testing/migration_e2e.py generate --replace
```

게시 전에는 `generate --baseline-only`로 공개 구버전만 준비할 수 있다. 신버전이 없는 사례는 `skippedUntilPublished`에 기록하며 로컬 빌드를 공개 릴리스로 간주하지 않는다. 다른 전환 버전을 검증할 때는 `--ipc-tag`, `--helper-tag`, `--replay-tag`를 명시한다. 기본 신버전 태그는 각각 `v0.4.2`, `v1.0.0`, `v1.0.0`이다.

각 Mods 세트에는 Helper/Replay의 유효한 `Settings.json`, `Data/migration-preservation.txt`와 Helper의 `Downloads/migration-preservation.txt`가 추가된다. Replay는 기본적으로 베타 수신을 켠 상태이며 사례 24만 끈다. 자동 녹화와 마이크/카메라는 꺼 두었으므로 새 권한 요청 없이 전환부터 확인할 수 있다. 이는 실제 사용자 DB나 녹화 파일의 전체 보존을 대신하지 않으며 아래 추가 검증으로 보완한다.

## 폴더 교체와 공통 실행 절차

1. 게임 프로세스를 완전히 종료한다. 원래 게임의 `Mods` 전체를 다른 이름으로 옮겨 백업한다. 게임 루트의 `.adofai-ipc-retired`가 기존 실사용 자료라면 함께 백업한다.
2. 확인할 사례의 `Mods`를 게임의 `Mods` 위치로 **복사**한다. 생성된 원본 세트는 다음 사례와 재현을 위해 보관한다. UMM이 필요한 설치는 기존 게임의 UMM 설치를 그대로 사용하고, 시험 중에는 다른 사용자 모드는 섞지 않는다.
3. 사례의 네트워크 조건을 적용한 뒤 게임을 실행한다. UMM에서 시험 대상 모드를 활성화하고 로그에 각 버전과 로드 결과가 남는지 본다. 실패 주입 사례 이외에는 1.0.0으로 전환되어 같은 실행부터 기능이 동작해야 한다.
4. **게임이 계속 실행 중인 상태**에서 `Mods/AdofaiIpc`의 존재를 확인한다. 모든 소비 모드 전환이 끝난 정상 사례는 폴더가 사라져야 한다. 전환이 막힌 사례는 IPC를 유지해야 한다. 확인은 메인 메뉴 도달 후 최대 약 60초 동안 한다.
5. 설정을 직접 바꾸기 전에 아래 읽기 전용 검사기를 실행한다. 로그, IPC 폴더 존재 여부, 이 결과를 첫 실행 증거로 저장한다. 파일 검사만으로 게임 IPC가 정상이라고 판정하지 않는다.
6. Helper 다운로드/설정 화면과 Replay 연결/재생 화면을 실제로 열어 본다. 짧은 레벨 플레이, 입력 녹화와 재생, 양쪽 웹 클라이언트의 연결 및 메시지 처리를 확인한다. 녹화 기능 시험을 위해 설정을 변경했다면 보존 검사에 해당 값이 의도적으로 달라졌다고 기록한다.
7. 게임을 완전히 종료하고 **동일한 게임 Mods를 유지한 채** 다시 실행한다. 새 SDK로 정상 실행되고 `Mods/AdofaiIpc`가 다시 설치되지 않는지 확인한다. `.adofai-ipc-retired`의 이전 잔여 파일 정리도 확인한다.
8. 다음 사례로 넘어갈 때 게임을 종료하고 시험용 Mods를 치운 뒤, 다음 원본 세트를 복사한다. 모든 검증을 마치면 처음 백업한 원래 Mods를 복원한다.

Replay 웹 기능 검증에는 [dev companion](https://tufreplay-dev.impl1113.dev/)을 연다. 2026-10-21 확인 시 해당 배포는 [SDK 매니페스트](https://tufreplay-dev.impl1113.dev/adofai-ipc.json)에 SDK `2.1.0`, protocol `3`을 제공했다. 같은 시점의 정식 도메인은 이 경로에 기존 HTML을 반환했으므로 이번 SDK 전환 시험은 dev 배포를 기준으로 한다. 웹 배포 상태는 [deployment.json](https://tufreplay-dev.impl1113.dev/deployment.json)에서 별도로 확인할 수 있다. 게임에서 웹을 자동으로 열어 주는 동작은 필요하지 않다.

예를 들어 사례 03으로 게임을 실행한 뒤 다음 명령의 `--mods`만 실제 게임 Mods의 절대 경로로 바꿔 실행한다. 검사기는 그 경로를 읽기만 한다.

```bash
rtk proxy python3 /Users/kgh/dev/src/adofai-ipc/scripts/testing/migration_e2e.py inspect \
  --case /Users/kgh/dev/src/adofai-ipc/build/migration-e2e/cases/03-both-old-ipc-old \
  --mods "/실제/게임/Mods" --expect-migrated
```

정상 전환은 `passed: true`, SDK 네 파일, 직접 진입점, `Runtime/state.json`의 `Current: 1.0.0` 및 빈 `Trial`, 보존 표본, IPC 부재를 확인한다. IPC를 유지해야 하는 실패 사례에는 `--expect-ipc-retained`를 쓴다. 최초 오프라인 설치 실패처럼 IPC 자체가 없는 사례는 두 옵션을 모두 생략해 보존만 검사한다. `retiredRemnants`는 삭제가 보류된 실제 파일 목록이다.

## 사례별 조건과 판정

대부분의 정상 사례는 첫 실행과 재실행을 모두 확인한다. 우선순위가 높은 사례는 **03, 07, 11, 14, 21, 22**다.

| 번호 | 준비된 Mods 조합 | 추가 실행 조건 | 기대 결과 |
| --- | --- | --- | --- |
| 01 | Helper 구버전 + IPC 0.4.1 | 온라인 | Helper 자동 전환, IPC 0.4.2 수신 및 실행 중 정리 |
| 02 | Replay 구버전 + IPC 0.4.1 | 온라인 | Replay 자동 전환, IPC 정리 |
| 03 | 둘 다 구버전 + IPC 0.4.1 | 온라인 | 세 제품 자동 업데이트, 두 모드 SDK 실행, IPC 정리 |
| 04 | 둘 다 구버전, IPC 없음 | 온라인 | 구버전 부트스트랩의 실제 자동 설치에서 시작해 전환, 다음 실행 재설치 없음 |
| 05 | Helper 구버전 + IPC 0.4.2 | 온라인 | 먼저 로드된 IPC가 Helper 전환을 기다리고 정리 |
| 06 | Replay 구버전 + IPC 0.4.2 | 온라인 | 먼저 로드된 IPC가 Replay 전환을 기다리고 정리 |
| 07 | 둘 다 구버전 + IPC 0.4.2 | 온라인 | 두 모드 완료 전에는 IPC 유지, 완료 후 실행 중 정리 |
| 08 | Helper 신버전 + Replay 구버전 + IPC 0.4.1 | 온라인 | 혼합 설치에서 같은 SDK 런타임으로 정상 동작 |
| 09 | Helper 구버전 + Replay 신버전 + IPC 0.4.1 | 온라인 | 반대 혼합 설치도 정상 전환 |
| 10 | 둘 다 신버전 + IPC 0.4.1 | 온라인 | IPC 자동 업데이트 후 이미 전환된 소비 모드 확인 및 정리 |
| 11 | 둘 다 신버전 + IPC 0.4.2 | 온라인 | 별도 사용자 조작 없이 첫 실행 중 IPC 정리 |
| 12 | Helper 신버전만 | 온라인 | 신규 수동 설치 ZIP만으로 SDK 실행, IPC 설치 없음 |
| 13 | Replay 신버전만 | 온라인 | 신규 수동 설치 ZIP만으로 SDK 실행, IPC 설치 없음 |
| 14 | 둘 다 신버전, IPC 없음 | 온라인 | 두 SDK 소비 모드 공존, IPC 설치 없음 |
| 15 | IPC 0.4.2만 | 온라인 | 소비 모드가 없어도 IPC 실행 중 정리 |
| 16 | 둘 다 신버전 + IPC 0.4.2 | `Info.LoadAfter`로 Helper → Replay → IPC 지정 | 순서를 로그에서 확인, SDK 공유와 IPC 정리 |
| 17 | 둘 다 신버전 + IPC 0.4.2 | `Info.LoadAfter`로 Replay → Helper → IPC 지정 | 역순도 정상. 16/17은 실제 ZIP의 진입점을 유지하고 순서 정보만 바꿈 |
| 18 | 둘 다 구버전 + IPC 0.4.1 | 실행 전 네트워크 끊기 | 자동 업데이트 실패 후 구버전 실행, IPC 유지. 온라인 재실행하면 정상 전환 |
| 19 | 둘 다 구버전, IPC 없음 | 실행 전 네트워크 끊기 | 의존성 설치 실패를 기록. 데이터 보존. 온라인 재실행 시 복구 |
| 20 | 둘 다 신버전, IPC 없음 | 실행 전 네트워크 끊기 | 업데이트 조회가 실패해도 동봉 SDK로 실행 |
| 21 | Helper 신버전 + Replay 구버전 + IPC 0.4.2 | 오프라인 | Replay 미전환 상태에서는 IPC 유지. 온라인 재실행 후 전환 및 정리 |
| 22 | 둘 다 신버전 + IPC 0.4.2, Replay Loader 전부 누락 | 네트워크 무관 | 불완전 설치를 완료로 보지 않으며 IPC 유지. Replay 오류 기록 후 원본 ZIP으로 복구 |
| 23 | 둘 다 구버전 + IPC 0.4.1, 잘린 다운로드 잔여물 | 온라인 | 임시 ZIP을 설치 완료본으로 채택하지 않고 정상 새 다운로드로 전환 |
| 24 | Replay 구버전 + IPC 0.4.1, 베타 수신 꺼짐 | 온라인 | 정식 1.0.0은 베타 설정과 관계없이 수신 |
| 25 | 둘 다 구버전 + IPC 0.4.1 | API 조회 허용, 릴리스 asset 호스트만 차단 | 다운로드 실패 후 구버전/IPC 유지. 차단 해제 후 재실행으로 전환 |
| 26 | Helper 신버전 + 이름이 `Renamed TUFReplay`인 구버전 Replay + IPC 0.4.2 | 오프라인 | 폴더 이름에 관계없이 `Info.Id`로 구버전 소비 모드를 찾아 IPC 유지. 온라인 재실행 후 전환 및 정리 |
| 27 | 두 신버전 모드 폴더 이름에 `Renamed ` 접두어 + IPC 0.4.2 | 온라인 | 사용자 지정 모드 폴더 이름에서도 SDK 배치, 시작, IPC 정리 및 다음 실행 정상 |

1.0.0 이후 자동 업데이트 검증에는 두 단계가 필요하다. 최초에는 위 실제 구버전 → 게시된 1.0.0을 확인한다. 다음 공개 버전이 나온 뒤 생성기의 신버전 태그를 그 버전으로 지정하고, `1.0.0` 설치를 별도 기준으로 보관해 새 런처의 업데이트도 확인한다. 아직 공개되지 않은 다음 버전을 실제 배포처럼 표시하지 않는다.

## 네트워크 및 불완전 패키지 실패 주입

사례 18~21은 게임 실행 전에 네트워크를 끊고, 첫 실행 관찰이 끝난 뒤 연결을 복구해 재실행한다. 실패가 발생했는지 로그로 확인한다. GitHub 응답이 캐시되거나 게임이 다른 네트워크를 사용했다면 오프라인 시험으로 판정하지 않는다.

사례 25용 프록시는 아래 명령으로 시작한다. `api.github.com`은 그대로 터널링하고 GitHub asset 호스트에는 502를 반환하며 TLS를 가로채지 않는다. 프록시 자체는 OS 설정을 바꾸지 않는다.

```bash
rtk proxy python3 /Users/kgh/dev/src/adofai-ipc/scripts/testing/migration_fault_proxy.py
```

테스트 게임 프로세스에만 HTTP/HTTPS 프록시 `127.0.0.1:8899`를 적용한다. 직접 실행하는 게임 프로세스에서는 `HTTP_PROXY`와 `HTTPS_PROXY` 환경변수를 사용할 수 있지만 Unity/Mono 버전에 따라 무시할 수 있다. 필요하면 별도 테스트 환경의 시스템 프록시를 사용하고 시험 직후 해제한다. 프록시 로그에 `ALLOW tunnel api.github.com`과 `BLOCK release asset host ...`가 모두 나타나고 게임 로그에 업데이트 다운로드 실패가 남아야 이 사례의 실패 주입이 성립한다. 프록시를 쓰지 않은 요청은 실패 주입으로 간주하지 않는다.

`fault-inputs`는 실제 게시 ZIP을 잘라 만든 손상 파일과 본체 DLL을 제거한 ZIP을 제공한다. 원래 체크섬/매니페스트를 함께 사용하면 검증에서 거부되어야 한다. 필수 파일 검증을 따로 시험하려면 테스트 하네스에서 손상 ZIP에 맞는 크기/해시를 주입하되 필수 본체 누락으로 거부되는지 본다. **배포 바이너리의 고정 GitHub HTTPS URL을 이 로컬 파일로 자동 리디렉션하지 않는다.** 게임에서 손상된 다운로드 응답 자체를 재현했다고 주장할 수 없으며, 이 항목은 주입 가능한 업데이터 하네스와 사례 22/23으로 분리해 판정한다. 공개 릴리스 파일을 테스트 목적으로 교체하지 않는다.

## 데이터 보존과 로그

생성된 표본은 업데이트가 `Settings.json`, `Data/`, `Downloads/`를 무분별하게 지우지 않는지 확인한다. 실제 기록 보존은 별도 시험용 사용자 데이터를 만들어 검증한다. 구버전으로 한 레벨을 플레이해 Replay 기록과 Helper 다운로드를 남기고 종료한 뒤, 해당 시험용 Mods를 복제해 두고 온라인 전환한다. 전환 후 같은 기록이 웹 UI에서 열리고 재생되며 Helper 파일과 선택한 저장 위치가 남아야 한다. SQLite는 정상 사용 중 변경되므로 바이트 해시 일치 대신 기록 ID/개수/재생 가능 여부를 확인한다. 원래 실사용 Mods나 DB를 이 시험의 입력으로 자동 복사하지 않는다.

각 실행에서 UMM 로그와 Unity Player 로그를 보관한다. 설치에 따라 위치가 달라질 수 있으므로 UMM 로그 창에서 실제 경로를 확인한다. 확인할 내용은 다음과 같다.

- Helper/Replay의 `[AutoUpdate]` 다운로드·설치·현재 버전·fallback 메시지 및 모드 로드 성공 여부.
- IPC의 `[AutoUpdate]`와 `[IpcRetirement]`. 정상: `Removed Mods/AdofaiIpc during this game session`. 보류: `Waiting for SDK migration` 또는 `Kept legacy IPC`와 그 원인.
- `Runtime/state.json`의 선택 버전과 `Trial` 정리, 루트 `Info.json`의 직접 SDK 진입점.
- SDK 시작/연결 오류, 같은 포트의 중복 바인딩 오류, API 메서드 미등록, 첫 실행 후 파일 재생성, 다음 실행의 IPC 자동 재설치 여부.
- 게임 실행 중 프레임 끊김, 로그 반복 폭주, UI/IPC 반응 지연. 파일 작업은 게임 프레임을 막지 않아야 한다.

Windows에서는 메모리 매핑된 DLL이나 삭제 공유를 허용하지 않은 핸들이 있으면 파일 삭제가 실패할 수 있다. [Microsoft DeleteFile 문서](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-deletefile)에 따른 OS 제약이다. 구현은 먼저 IPC 폴더를 Mods 밖의 `.adofai-ipc-retired/AdofaiIpc-<id>`로 옮긴 뒤 즉시 삭제를 시도한다. 성공 판정은 **실행 중 원래 Mods 경로가 사라짐**, **현재 실행 정상**, **다음 실행 IPC 재설치 없음**을 각각 기록한다. Windows에서 잠긴 잔여 바이트가 남았다면 완전 삭제 성공으로 기록하지 말고 SDK 소비 모드의 다음 시작 시 정리되는지 확인한다. 사례 15처럼 SDK 소비 모드가 하나도 없으면 이 후속 정리 코드도 실행되지 않으므로 Mods 밖의 잔여 파일이 자동으로 정리된다고 단정하지 않는다. 디렉터리 이동 자체가 실패하면 원본 IPC를 보존하고 실패 원인을 로그로 남겨야 한다. 사용자 지정 Mods 경로, 다른 볼륨, 권한 제한에서도 확인한다.

## 자동 확인과 수동 확인의 경계

생성기가 자동 확인한 것은 공개 릴리스 존재, asset digest와 ZIP SHA-256/크기, 체크섬/매니페스트 일치, ZIP CRC와 안전한 경로, 모드 ID/버전, 실제 폴더 세트 생성이다. 저장소의 업데이터/이전 진입점 테스트 결과는 각 제품의 별도 테스트 로그를 따른다.

Unity 게임의 실제 로드 순서, 같은 실행의 기능, 설정 UI, 기록 재생, 프레임 영향, 실행 중 폴더 소멸, Windows 파일 잠금, 두 번째 실행의 재설치 방지는 사용자 게임 E2E 항목이다. `report.json`은 게임을 실행한 것처럼 자동 갱신하지 않는다. 사례별로 OS/게임 버전, 첫 실행 결과, 재실행 결과, 보존 결과, IPC 원래 경로/잔여 경로, 로그 경로를 기록한다.
