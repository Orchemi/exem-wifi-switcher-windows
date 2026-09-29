# 동작·설정·개발 참고

## 사용 흐름

```text
연결 SSID 확인 → 연속 관측으로 안정화 → 프로필 선택 → 현재 구성 비교
                                                   ↓
                         observe: 적용 예정 상태만 기록
                         enforce: 대상 NIC만 변경 → 재조회로 검증
```

- 등록한 SSID는 해당 고정 IP 또는 DHCP 프로필을 사용합니다.
- 등록하지 않은 SSID는 기본적으로 DHCP입니다. `fallback: "hold"`로 변경 보류도 가능합니다.
- SSID를 읽지 못함·미연결·권한 거부는 외부망으로 간주하지 않습니다. 변경을 보류합니다.
- IP, 게이트웨이, DNS를 함께 비교합니다. 적용 명령 성공만으로 완료라고 판단하지 않습니다.
- 반복 실패와 반영 실패를 제한합니다. 사용자 또는 다른 관리 도구와 무한히 설정을 덮어쓰지 않습니다.
- IPv6, VPN, 프록시, 방화벽, 유선 LAN은 관리하지 않습니다. IPv4 설정 전환은 망 분리나 접근 통제 기능이 아닙니다.

## 설치되는 것과 권한

| 대상 | 위치 / 용도 |
|---|---|
| 프로그램·런타임·스크립트 | `%ProgramFiles%\WifiProfileSwitcher` |
| 사용자 설정 | `%ProgramData%\WifiProfileSwitcher\config.json` |
| 최초 변경 전 백업 | 같은 디렉터리의 `snapshot.json` — 자동 덮어쓰기 안 함 |
| 최근 상태 | 같은 디렉터리의 `status.json` — 네트워크 실값을 기록하지 않음 |
| 서비스 | `WifiProfileSwitcher`, LocalSystem, 지연 자동 시작 |

설치 화면은 감지된 설정과 변경 내용을 표시한 뒤 버튼으로 승인을 받습니다.
설치는 관찰 모드로 저장하고, GUI가 관찰 서비스를 시작합니다. 자동 전환은 별도 승인 전까지 꺼져 있습니다.
스크립트를 터미널에서 직접 실행하는 경우에는 기존 확인 문자열 입력 방식을 유지합니다.
프로그램 파일은 일반 사용자 읽기만, 설정 디렉터리는 관리자와 SYSTEM만 접근하도록 제한합니다.
네트워크 변경 권한 때문에 프로토타입 서비스는 높은 권한을 사용합니다. 조직에서 승인한 PC에서만 설치하세요.

위치 권한, PowerShell 정책, AppLocker/WDAC 등의 제한은 [권한과 해결 방법](PERMISSIONS.md)에 정리했습니다.
설치 승인 후 프로그램의 PowerShell 자식 프로세스에만 `RemoteSigned`를 지정합니다.
컴퓨터·사용자의 영구 실행 정책은 바꾸지 않으며 그룹 정책이 우선합니다.
정책을 강제로 해제하거나 보안 제품을 끄지 않습니다. 서비스 계정에서 SSID 조회가 막히면
관리자 사용자 세션에서 `watch`를 실행해 비교할 수 있습니다. 이 대안은 터미널을 유지해야 하며,
로그인 작업 자동 등록은 이번 프로토타입에 포함하지 않습니다.

## 설정

`config.example.json`은 문서용 예시입니다. 실제 네트워크에 그대로 적용하지 마세요.
`adapterId`는 로컬에서 `WifiProfileSwitcher.exe adapters`로 확인한 Wi-Fi GUID로 바꿉니다.
고정 IP·접두사 길이·게이트웨이·DNS는 본인 장비에 배정된 값을 입력합니다.
프로필 복사로 여러 PC에 같은 고정 IP를 배포하면 충돌합니다.

설정 예시의 `/24`는 서브넷 마스크 `255.255.255.0`을 뜻합니다.
초기 버전은 단일 IPv4·기본 게이트웨이 구성만 지원합니다. 복수 주소·사용자 지정 경로는 변경을 거부합니다.
SSID는 대소문자를 구분합니다. 잘못된 필드·중복 SSID·잘못된 IP·서브넷이 맞지 않는 게이트웨이를 검증합니다.

## 중지·복구·제거

Setup.exe를 다시 열어 중지·복구·제거 버튼을 사용하세요. 자세한 내용은 [설치 화면 시험 안내](GUI-TESTING.md)에 있습니다.

<details>
<summary>기존 ZIP의 터미널 복구 방법</summary>

관리자 PowerShell에서:

```powershell
Stop-Service WifiProfileSwitcher
$app = Join-Path $env:ProgramFiles 'WifiProfileSwitcher\WifiProfileSwitcher.exe'
& $app recover-dhcp --confirm
# 또는 최초 백업 당시 네트워크로 돌아온 뒤:
# & $app restore --confirm
```

두 복구 명령은 `RECOVER` 입력을 추가로 요구합니다. 서비스가 실행 중이면 거부합니다.
복구는 네트워크 연결을 끊을 수 있으므로 원격 접속만 가능한 PC에서 시험하지 마세요.
서비스를 중지하는 것만으로 현재 IP가 바뀌지는 않습니다.

```powershell
& (Join-Path $env:ProgramFiles 'WifiProfileSwitcher\scripts\uninstall.ps1')
```

제거도 IP를 자동 변경하지 않습니다. 필요한 복구를 먼저 수행하세요. 상세 삭제 범위는 시험 문서를 확인하세요.

</details>

## 개발·검증

.NET 10 SDK와 PowerShell 7을 사용합니다. Windows 앱은 macOS에서도 크로스 빌드할 수 있습니다.

```sh
dotnet test tests/WifiProfileSwitcher.Core.Tests -c Release
dotnet build src/WifiProfileSwitcher.Windows -c Release
pwsh -NoProfile -File scripts/check.ps1
pwsh -NoProfile -File tests/NetworkBackend.Tests.ps1
pwsh -NoProfile -File scripts/package-setup.ps1
```

`src/WifiProfileSwitcher.Core`는 순수 설정 검증·판정·재시도 정책,
`src/WifiProfileSwitcher.Windows`는 Windows SSID 조회·서비스·진단,
`scripts/network.ps1`은 제한된 IPv4 읽기·변경·복구를 담당합니다.
실제 설정을 명령 문자열에 끼워 넣지 않고 JSON 표준 입력으로 전달합니다.

## 공개 정보와 배포 신뢰

실제 설정, 백업, 원본 네트워크 출력, IP, MAC, 내부 도메인, 개인 경로는 공개하지 않습니다.
피드백에는 `doctor`와 `status`의 상태 코드 및 시험 결과만 포함하세요. `adapters` 출력은 로컬 설정용입니다.
자세한 기준은 [RULES.md](../RULES.md)를 따릅니다. 외부 전송·텔레메트리·자동 업데이트 기능은 없습니다.

초기 패키지는 Authenticode 서명되지 않습니다. Windows 또는 조직 정책이 실행을 막을 수 있습니다.
SHA-256은 파일 일치 확인 수단이며 서명이나 안전성 보증을 대신하지 않습니다.
원본·해시를 확인해도 조직 정책이 차단하면 관리자에게 승인을 요청하세요.

## 기술 근거

- [Windows 서비스와 .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service)
- [Wi-Fi API의 위치 권한 제한과 SSID 전용 API 안내](https://learn.microsoft.com/en-us/windows/win32/nativewifi/wi-fi-access-location-changes)
- [연결 SSID 조회](https://learn.microsoft.com/en-us/uwp/api/windows.networking.connectivity.wlanconnectionprofiledetails.getconnectedssid)
- [IPv4 구성](https://learn.microsoft.com/en-us/powershell/module/nettcpip/new-netipaddress)
- [DNS 설정과 자동 DNS 복원](https://learn.microsoft.com/en-us/powershell/module/dnsclient/set-dnsclientserveraddress)

macOS판 [exem-wifi-switcher](https://github.com/Orchemi/exem-wifi-switcher)의 전환 정책을 바탕으로 별도 구현합니다.

## Windows 버전

프로젝트의 Windows API 대상은 Windows 10 빌드 19041입니다. Windows 11만 허용하는 검사는 없습니다. 배포 파일은 x64용이며, 모든 Windows 버전에서의 동작을 보증하지 않습니다. .NET 10의 공식 지원 범위는 [지원 OS 목록](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md)을 참고하세요.
