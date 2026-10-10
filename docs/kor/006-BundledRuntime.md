# 동봉 런타임과 버전 계약

[목차](000-DevelopGuide.md) | [English](../en/006-BundledRuntime.md)

각 모드는 시작하기 문서의 동일한 배치로 DLL을 동봉합니다.
로더는 Mods/<mod>/ipc/manifest.json만 탐색하고 후보를 로드하지 않은 채 DLL 메타데이터를 확인합니다.
비활성 모드의 동봉 파일도 후보이므로 켜는 순서가 구버전 선택으로 이어지지 않습니다.
선택한 DLL만 로드하며 잘못된 후보는 Trace 진단을 남기고 제외합니다.

manifest schema 1, 계약 major 1, 계약 어셈블리 식별 버전 1.0.0.0, wire major 3이 일치하는
후보 중 가장 높은 안정 SemVer를 선택합니다. prerelease는 자동 선택하지 않습니다.
같은 버전이면 전체 경로의 ordinal 순서로 결정합니다.
모드는 최소 런타임 버전·계약/wire major·필수 capability를 선언합니다.
요구사항 실패는 호스트를 시작하거나 오염시키지 않으며 다른 호환 모드는 계속 사용할 수 있습니다.

AdofaiIpc.Runtime.dll 옆에 다음 manifest를 배포합니다.

```json
{
  "SchemaVersion": 1,
  "RuntimeVersion": "2.0.0",
  "ContractMajor": 1,
  "ContractAssemblyVersion": "1.0.0.0",
  "WireMajor": 3,
  "Capabilities": ["commands", "game-thread", "snapshots", "local-peer", "downloads"]
}
```

시작 전에 런타임의 계약 어셈블리 참조와 Describe() 결과를 manifest와 비교하고,
시작 후 반환한 런타임도 검사합니다. DLL을 로드한 뒤 실패하면 게임 재시작 전까지 실패를 유지합니다.
다른 버전 DLL로 fallback하지 않습니다. manifest만 따로 수정하지 마세요.

로더 사본별 static 대신 공용 AppDomain 슬롯과 interned lock으로 조율합니다.
계약 참조는 같은 Assembly 인스턴스로 모여야 합니다. 계약 중복 로드와 선택 전 런타임 로드를 거부합니다.
계약 DLL을 Assembly.Load(byte[])로 로드하거나 소비 모드에 병합하지 마세요.
소비 모드는 Runtime을 직접 참조하지 않습니다. 게임의 단일 scripting AppDomain을 대상으로 하며
서로 독립된 AppDomain은 지원하지 않습니다.

선택 뒤 모든 모드는 같은 런타임을 얻습니다. 더 높은 버전을 새로 설치했다면 게임을 완전히 재시작합니다.
늦게 켠 모드가 더 높은 최소 버전이나 없는 capability를 요구하면 그 모드의 IPC 설정만 실패합니다.
기존 등록과 연결은 유지합니다. v2에는 자동설치·다운로드·UMM 의존 모드 활성화·자체 업데이트·
trial/rollback 상태·migration이 없습니다.

릴리즈 계약:

- Contracts와 Loader의 AssemblyVersion은 같은 계약 major에서 1.0.0.0을 유지합니다.
  FileVersion과 InformationalVersion으로 제품 릴리즈를 구분하며 모든 bundle에 같은 계약 ABI를 배포합니다.
- major 1에서는 공개 멤버와 서명을 유지합니다. 기존 인터페이스에 필수 멤버를 추가하지 않습니다.
  향후 확장은 별도 extension interface와 capability 협상으로 설계합니다.
- API 호환성을 깨면 계약 major와 어셈블리 식별을 변경하고 선택 정책도 명시적으로 바꿉니다.
  현재 로더는 major 1만 지원하며 비호환 major를 동시에 호스팅하지 않습니다.
- wire와 기능별 메시지 major는 별개입니다. namespace 수명 payload의 featureProtocolMajor를
  도메인 client에서 확인하고 명령 호환성을 판단합니다.
- 이전에 빌드한 소비 어댑터를 새 런타임에서 검증합니다. 로드 순서·끄기/켜기·취소·재연결을 포함하며
  CLR 테스트만으로 Unity/Mono 호환성을 확정하지 않습니다.

릴리즈 전에는 실제 게임에서 두 소비 모드 시나리오를 실행해야 합니다.
UMM이 루트의 Contracts/Loader 참조를 하나의 계약 인스턴스로 해석하는지,
Unity synchronization context에서 시작하는지, quitting/unloading이 호스트를 종료하는지 확인하세요.
자동 테스트는 .NET 8에서 실행하며 ADOFAI나 native Mono 로더를 실행하지 않습니다.
