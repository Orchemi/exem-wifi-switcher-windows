# v0.2.0-alpha.2

[Windows 설치 파일 다운로드](https://github.com/Orchemi/exem-wifi-switcher-windows/releases/download/v0.2.0-alpha.2/WifiProfileSwitcher-Setup-0.2.0-alpha.2.exe)

Windows 11 x64용 GUI 시험판입니다. EXE를 더블클릭하면 설치와 설정을 진행할 수 있습니다. 터미널과 .NET 별도 설치는 필요 없습니다.

## 변경 사항

- 회사 Wi-Fi의 기존 고정 IP·DNS를 자동으로 가져옵니다.
- 감지 실패 시 회사 Wi-Fi 이름·고정 IP·서브넷·게이트웨이·DNS를 직접 입력할 수 있습니다.
- 저장과 자동 전환 켜기를 분리했습니다. 저장만으로 현재 IP를 변경하지 않습니다.
- 복구·제거·오류 정보는 ‘더 보기’에 모았습니다.

## 시험 범위

실제 Windows Wi-Fi 장비에서 전환·절전 복귀 검증은 아직 필요합니다. Wi-Fi 이름을 읽을 수 없으면 자동 전환을 켜지 않습니다. 관리자 승인이 필요하며 서명되지 않은 EXE라 Windows 경고가 나올 수 있습니다. 회사 정책을 해제하지 말고 관리자에게 승인을 요청하세요.

## 이전 시험판에서 이동

기존 설치 프로그램은 자동 업데이트되지 않습니다. 필요한 복구를 먼저 수행하고 설정을 기록한 뒤 기존 앱을 제거하고 새 EXE로 설치하세요. 제거하면 설정과 최초 백업이 삭제되며 현재 IP는 유지됩니다.

[이미지 설치 안내](https://github.com/Orchemi/exem-wifi-switcher-windows#readme) · [변경 내역](https://github.com/Orchemi/exem-wifi-switcher-windows/compare/v0.1.0-alpha.1...v0.2.0-alpha.2)
