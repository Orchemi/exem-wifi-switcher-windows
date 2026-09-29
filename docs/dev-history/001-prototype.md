# 001: Windows 시험용 프로토타입

관련 계획: [001-prototype](../plan/001-prototype.md)

## 구현과 검증 범위

새 저장소에서 순수 전환 정책, Windows 서비스/CLI, 제한된 PowerShell 네트워크 백엔드,
보호된 설치·설정 교체·제거, 동료 시험 안내를 구현했다.
기존 macOS 저장소와 실제 네트워크 설정은 변경하지 않았다.

초기 API/테스트 구성의 실패를 확인한 뒤 Core를 구현했다. 네트워크 백엔드는 모의 명령으로
복구 전 검증, 다른 어댑터/IPv6 제외, 부분 실패, 영구 설정 정리와 백업 보존을 확인했다.
설치 스크립트는 macOS에서 구문·경로·관찰 모드 강제·원자 저장을 검사하고,
실제 ACL/서비스 검증은 Windows CI와 실기 시험으로 분리했다.

## 로컬 품질 게이트

| 명령 | 결과 |
|---|---|
| `dotnet test tests/WifiProfileSwitcher.Core.Tests -c Release` | 39개 통과 |
| `dotnet build src/WifiProfileSwitcher.Windows -c Release` | 경고 0, 오류 0 |
| `pwsh -NoProfile -File tests/NetworkBackend.Tests.ps1` | 모의 네트워크 테스트 통과 |
| `pwsh -NoProfile -File tests/Installer.Tests.ps1` | 구문·경로·설정 저장 검사 통과 |
| `pwsh -NoProfile -File scripts/check.ps1` | 소스 개인정보/스크립트 구문 검사 통과 |
| `pwsh -NoProfile -File scripts/package.ps1` | Windows x64 자체 런타임 포함 ZIP 생성 및 배포물 검사 통과 |
| `dotnet list src/WifiProfileSwitcher.Windows package --vulnerable --include-transitive` | 알려진 취약 패키지 없음 (조회 시점 기준) |

Windows CI는 같은 테스트와 읽기 전용 EXE smoke, 설치→관찰 서비스→설정 교체→제거를 실행한다.
CI 결과의 단일 출처는 해당 커밋의 Actions 실행이다. CI의 가상 어댑터 환경에서
실제 Wi-Fi를 변경하거나 내부 네트워크에 접속하지 않는다.

## 검토에서 수정한 사항

- SSID 미확인과 외부망 접속을 구분하고, 네트워크 상태 미확인에서도 쓰기를 보류.
- DHCP/DAD 대기 30초, 실패 backoff, 같은 SSID에서 재적용 총량 제한.
- 설치 실패 정리는 이번 실행에서 만든 디렉터리만 대상으로 제한.
- PowerShell 파일 경로 부모 탐색과 ACL 소유자 SID 처리 보완.
- 백업 값 검증을 IP 변경보다 먼저 수행. DHCP+수동 DNS, 고정 IP+자동 DNS 복원 처리.
- 서비스와 foreground 쓰기는 파일 잠금으로 직렬화. 비동기 코드에서 thread-affine mutex 미사용.
- 임의 예외 전문이나 사내 값 대신 허용한 상태 코드만 진단 경계 밖으로 전달.
- Windows PowerShell용 한국어 설치 스크립트는 UTF-8 BOM으로 저장.
- 프로세스 한정 RemoteSigned를 설치 계획에 명시. 영구 정책·그룹 정책·보안 제품은 변경하지 않음.

## 실기 시험이 필요한 항목

- 회사 PC의 서비스 계정/사용자 세션에서 연결 SSID가 조회되는지.
- 고정 IP↔DHCP·DNS 왕복, IP 중복, DHCP 임대 지연, 무선랜 드라이버별 결과.
- 재부팅·로그인 전후·절전 복귀·Wi-Fi 끊김 및 재접속.
- 조직 정책, Authenticode/SmartScreen, AppLocker/WDAC, 위치 권한과 UAC.
- 실제 장비에서 설치·거부·설정 교체 실패·복구·완전 제거.

SSID 최종 확인과 OS의 IPv4 변경은 원자적 작업이 아니다. 변경 도중 다른 SSID로 이동하면
잠시 이전 프로필이 적용될 수 있다. 첫 시험에서 빠른 연속 전환을 피하고 물리 복구 수단을 확보한다.
백업은 단순 IPv4/DNS용이며 고급 라우팅 메트릭, VPN, IPv6 등 전체 네트워크 복원본이 아니다.
사용자 세션 대안은 foreground `watch`까지만 제공한다. 자동 로그인 작업/IPC 분리는 후속 실증 결과로 결정한다.
