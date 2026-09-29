# 002-01: 설치 화면과 현재 고정 IP 가져오기

## 관련 문서

- [구현 계획](../plan/002-gui-setup.md)
- [설치 화면 시험 안내](../GUI-TESTING.md)

## [DEV] 개발: 2026-09-29

Setup.exe 하나로 현재 회사 Wi-Fi의 고정 IPv4·서브넷·게이트웨이·DNS를 가져오고, 확인 후 관찰 모드로 설치하도록 구현했습니다.
입력은 확인 체크와 버튼 승인뿐이며, 네트워크 숫자 입력·JSON 편집·터미널 실행은 필요하지 않습니다.
GUI는 기존 설치 스크립트를 숨겨진 자식 프로세스로 호출합니다. 설치 로직을 별도로 복제하지 않았습니다.

자동 감지는 DHCP lease·자동 DNS·복수 주소·사용자 지정 경로를 거부합니다.
설치·활성화 직전 실제 값을 재조회하고, 활성화 전에 서비스를 관찰 모드로 재시작합니다.
최신 서비스 상태가 현재 설정과 일치하고 변경 시도가 없어야 실제 자동 전환을 허용합니다.

## 변경 범위

- Core: InitialProfileCapture, SetupReadiness와 단위 테스트
- Windows: 읽기 전용 capture 프로토콜
- Setup: WinForms WinExe, 관리자 manifest, 보호된 payload 추출, 자동 감지·승인·설치·활성화·중지·복구·제거
- PowerShell: GUI 확인 스위치, 제한된 서비스 제어, 설치·네트워크 테스트
- 배포: 단일 EXE 생성, 런타임 고지 포함, SHA-256, Windows CI에 패키지·네이티브 컨트롤 smoke 추가
- 문서: GUI 시험 절차, 기존 ZIP 절차 구분

## 품질 게이트

- 자동 감지 테스트는 API 미구현 실패를 먼저 확인한 뒤 구현했습니다.
- 서비스 관찰 증거 테스트는 타입 미구현 실패 후 구현하여 최신 상태·schema·관찰 모드·변경 없음 조건을 검증했습니다.
- `dotnet test tests/WifiProfileSwitcher.Core.Tests -c Release`: 51개 통과.
- `dotnet build src/WifiProfileSwitcher.Setup -c Release`: 경고·오류 없음.
- `pwsh -NoProfile -File tests/NetworkBackend.Tests.ps1`: 통과. 캡처 중 네트워크 변경 없음 확인.
- `pwsh -NoProfile -File tests/Installer.Tests.ps1`: 통과. GUI 콘솔 입력 생략과 기존 CLI 확인 유지.
- `pwsh -NoProfile -File scripts/check.ps1`: PowerShell 구문·공개 소스 개인정보 검사 통과.
- `pwsh -NoProfile -File scripts/package-setup.ps1`: 자체 포함 Windows x64 GUI EXE와 SHA-256 생성.
- 문서 상대 링크 검사, 존재하지 않는 링크를 통한 검사기 검증, `git diff --check`: 통과.

## [QA] 정적 검토: 2026-09-29

별도 검토에서 설치 payload 목록 불일치, 기존 enforce 프로세스를 관찰 검증에 재사용할 가능성, 제거 시 백업 삭제 안내 불일치를 발견했습니다.
불필요한 스크립트 dispatcher를 제거하고, 관찰 모드 확인·서비스 재시작·최신 상태 검사 및 실제 삭제 범위 안내로 수정했습니다.
재검토에서 추가로 수정이 필요한 네트워크 변경 위험은 발견하지 못했습니다.

## 남은 실기 검증

Mac에서 생성한 초안입니다. 이번 변경의 Windows CI 실행, 실제 UAC·GUI 배치·보호 경로 추출·서비스 설치·위치 권한·Wi-Fi 전환은 아직 검증하지 않았습니다.
CI 정의는 준비했으나 이 변경을 원격에 push하지 않았으므로 통과했다고 주장하지 않습니다.
불완전한 설치 디렉터리가 남는 강제 종료 상황에서는 자동 덮어쓰기 대신 중단하며 관리자 확인이 필요할 수 있습니다.
서명되지 않은 실행 파일에 대한 Windows 경고와 조직 정책은 동료 환경에서 확인해야 합니다.
