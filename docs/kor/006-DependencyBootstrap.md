# 의존 모드 Bootstrap

의존 모드는 고정 `AdofaiIpc.DependencyShim.dll`, 세 필드만 가진
`AdofaiIpcBootstrap.json`, 버전별 bootstrap 후보를 패키지합니다.

```text
AdofaiIpc.DependencyShim.dll
AdofaiIpcBootstrap.json
DependencyBootstrap/state.json
DependencyBootstrap/versions/0.3.0/AdofaiIpc.Bootstrap.dll
```

`Info.json`은 `AdofaiIpc.DependencyShim.DependencyShim.Load`를 가리킵니다. manifest는
`MinimumAdofaiIpcVersion`, `AssemblyName`, `EntryMethod`만 선언합니다. 예전 다운로드 URL
필드는 읽을 수 있지만 무시하며, 공식 릴리스 URL은 bootstrap이 관리합니다.

shim은 `Trial`을 먼저 호출하고 성공하면 승격합니다. 예외나 `false`가 반환되면 `Current`,
`Previous` 순서로 fallback합니다. 모드 updater는 전체 릴리스 패키지 검증 뒤
`StageCandidate(modRoot, assemblyPath)`를 호출하고, 해당 모드 runtime을 rollback할 때
`DiscardTrial(modRoot, version)`을 호출합니다. 이미 로드된 DLL은 덮어쓰지 않으며 trial은
다음 게임 실행부터 활성화됩니다.

AdofaiIpc가 비활성화됐거나 오래됐거나 로드에 실패하면 의존 모드 core를 호출하지 않습니다.
미설치 상태에서는 프로세스당 한 번 자동 설치를 시도합니다. 실패하거나 다른 의존성 오류가
있으면 영향을 받은 모든 모드를 retained Unity uGUI 하나에 모아 표시합니다. 정상 경로에는
GameObject, event 구독, coroutine, 프레임별 callback이 없습니다.

최초 전환 릴리스는 공용 `AdofaiIpc.Migration.dll`로 고정 shim과 bootstrap을 설치하고,
그 실행에서는 의존 모드 core를 시작하지 않은 채 영향을 받은 모드 목록을 안내창 하나에
표시합니다. 사용자는 AdofaiIpc만 다시 설치하고 게임을 완전히 종료한 뒤 재실행하면 됩니다.
신규 설치는 처음부터 최종 구조를 사용하므로 별도 전환이 필요하지 않습니다.
