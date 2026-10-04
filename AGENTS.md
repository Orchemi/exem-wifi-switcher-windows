# Wi-Fi Profile Switcher for Windows

공개 저장소다. RULES.md와 docs/plan/001-prototype.md를 먼저 읽는다.
실제 사내 네트워크 값·사용자 이름·로컬 경로·토큰·진단 원문은 소스, 테스트, 문서, 배포물에 넣지 않는다.
문서용 주소만 예시에 사용한다. 실제 설정과 백업은 설치된 PC에만 둔다.

- .NET 10, C#, PowerShell. Windows 11에서 검증하는 시험용 프로토타입이다.
- 순수 정책은 Core, Windows API와 명령 실행은 Windows 프로젝트에 둔다.
- 기본 관찰 모드. 쓰기는 명시적인 설정 및 관리자 승인 뒤에만 한다.
- 대상 어댑터 GUID 고정, 미확인 연결에서는 변경 보류, 동시 실행 금지.
- 커밋: `<type>: <한글 요약>`. 귀속/생성 도구 문구 금지.
- 소스·배포물은 공개 전제이며, Windows 실기 미검증 상태를 README와 시험 패키지에 남긴다.

## Git workflow

- `main`이 base다. 문서·기능 변경은 이슈 → `<type>/<이슈번호>` 작업 브랜치 → PR 순서로 진행하고
  보호 브랜치에 직접 커밋하지 않는다.
- 커밋·push·merge는 현재 작업의 승인 범위에서만 수행한다. 한글 conventional commit과 도구 귀속
  문구 금지 규칙을 지킨다.

## 검증

`dotnet test tests/WifiProfileSwitcher.Core.Tests -c Release`
`dotnet build src/WifiProfileSwitcher.Windows -c Release`
`pwsh -NoProfile -File scripts/check.ps1`
Windows에서 검증하지 못한 항목은 검증했다고 쓰지 않는다.
