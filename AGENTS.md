# Wi-Fi Profile Switcher for Windows

공개 저장소다. RULES.md와 docs/plan/001-prototype.md를 먼저 읽는다.
실제 사내 네트워크 값·사용자 이름·로컬 경로·토큰·진단 원문은 소스, 테스트, 문서, 배포물에 넣지 않는다.
문서용 주소만 예시에 사용한다. 실제 설정과 백업은 설치된 PC에만 둔다.

- .NET 10, C#, PowerShell. Windows 11에서 실기 검증하는 초기 프로토타입.
- 순수 정책은 Core, Windows API와 명령 실행은 Windows 프로젝트에 둔다.
- 기본 관찰 모드. 쓰기는 명시적인 설정 및 관리자 승인 뒤에만 한다.
- 대상 어댑터 GUID 고정, 미확인 연결에서는 변경 보류, 동시 실행 금지.
- 커밋: `<type>: <한글 요약>`. 귀속/생성 도구 문구 금지.
- 이 초기 저장소 생성·공개 및 시험용 패키지 제공은 사용자의 현재 요청 범위다.
- 이후 커밋·push·배포는 명시적 요청에 따른다.

## Git workflow

standalone: true
사유: 기존 원격 브랜치가 없는 신규 프로젝트의 초기 공개 작업이다.
초기 검증과 개인정보 검사를 마친 파일만 명시적으로 stage하여 main의 최초 커밋을 만들고,
Orchemi/exem-wifi-switcher-windows에 push한다. 이후 기능 변경은 별도 브랜치/PR을 사용한다.

## 검증

`dotnet test tests/WifiProfileSwitcher.Core.Tests -c Release`
`dotnet build src/WifiProfileSwitcher.Windows -c Release`
`pwsh -NoProfile -File scripts/check.ps1`
Windows에서 검증하지 못한 항목은 검증했다고 쓰지 않는다.
