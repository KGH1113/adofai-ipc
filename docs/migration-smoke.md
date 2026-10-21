# 구버전 → 신버전 확인

정상 인터넷 연결에서 **한 세트만**, 게임 실행 두 번으로 확인한다.

## 현재 테스트 설치를 그대로 사용하는 경우

Helper와 Replay가 이미 1.0.0이고 IPC의 `Update/state.json`이 0.4.2라면,
폴더를 다시 교체하지 않고 게임을 실행한다. IPC는 0.4.3을 받아 실제 새 DLL을 실행하고,
`Info.json`을 0.4.3으로 갱신한 뒤 실행 중 기존 IPC 폴더를 제거해야 한다.

## 처음부터 구버전 조합으로 확인하는 경우

준비된 폴더는 `build/migration-smoke/cases/03-both-old-ipc-old/Mods`다.
실제 공개된 Helper 0.1.9, Replay 0.2.0-beta.4, IPC 0.4.1이 들어 있다.

1. 게임을 종료하고 기존 Mods를 백업한 뒤 이 세트의 Mods를 복사한다.
2. 인터넷이 연결된 상태로 게임을 실행한다.
3. Helper와 Replay가 1.0.0으로 실행되고 웹 연결·기본 기능이 작동하는지 확인한다.
4. 게임 실행 중 `Mods/AdofaiIpc`가 사라지는지 확인한다.
5. 게임을 종료하고 같은 폴더로 다시 실행한다. 두 모드가 정상이며 IPC가 재설치되지 않으면 통과다.

Replay는 main 병합 전까지 <https://tufreplay-dev.impl1113.dev/>에서 확인한다.

로그에는 `Running IPC runtime 0.4.3.0` 및 `[IpcRetirement] Removed Mods/AdofaiIpc`가 남아야 한다.
UMM이 시작할 때 읽은 `Version '0.4.1'. Loading` 한 줄은 업데이트 전 표시다.
실제 선택 버전은 위 런타임 로그와 업데이트 상태로 확인한다.

세트를 다시 만들 때:

```bash
rtk proxy python3 scripts/testing/migration_e2e.py generate --smoke --replace
```

생성기와 자동 검사는 실제 게임 Mods를 변경하지 않는다. Unity Mono에서 공개 0.4.1·0.4.2
DLL이 먼저 로드된 경우의 패키지 검증·새 코드 선택·메타데이터 갱신·삭제는 자동 검사한다.
실제 게임 기능과 Windows의 로드된 파일 잠금 동작은 이 수동 절차로 별도 확인한다.
