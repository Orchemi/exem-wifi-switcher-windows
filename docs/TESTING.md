# 동료용 Windows 1차 시험 절차

이 문서는 Windows 11 실기에서 시험용 패키지를 확인하기 위한 절차다. 개발 환경은 macOS라서 아래 서비스·무선랜·관리자 권한 동작은 아직 검증하지 않았다. 첫 시험은 원격 접속만으로 진행하지 말고, 화면과 네트워크 복구 수단을 함께 확인할 수 있는 물리 PC에서 진행한다.

## 시험 전 약속

- 시험 대상은 Wi-Fi 어댑터 하나만 지정한다. 유선 LAN, VPN, 가상 어댑터를 `adapterId`에 넣지 않는다.
- 내부망 SSID와 외부망 SSID, 실제 IP·게이트웨이·DNS·MAC·사용자명은 이 저장소, 압축 파일, 이슈, 로그에 넣지 않는다. 설정 파일은 시험 PC의 보호된 `ProgramData`에만 둔다.
- 처음에는 반드시 `observe` 모드로 확인한다. 이 모드에서는 연결 SSID와 판정 결과만 기록하고 IP/DNS를 바꾸지 않는다.
- `enforce` 시험은 네트워크가 끊겨도 복구할 수 있는 로컬 관리자 계정과 유선 또는 별도 접속 수단을 준비한 뒤 진행한다. 업무 중인 주 네트워크에서 첫 시험을 하지 않는다.
- 회사의 WDAC, AppLocker, 실행 정책, 백신, MDM, 위치 권한 정책이 실행·SSID 조회·서비스 등록을 막을 수 있다. 정책을 우회하거나 실행 정책을 바꾸지 말고 관리자에게 허용된 서명·배포 절차를 문의한다.

## 패키지 확인

압축 해제 후 패키지 루트에 `WifiProfileSwitcher.exe`, `config.example.json`, `scripts\install.ps1`, `scripts\configure.ps1`, `scripts\uninstall.ps1`, `scripts\network.ps1`가 있는지 확인한다. 출처가 불명확한 실행 파일은 관리자 PowerShell로 실행하지 않는다.

일반 PowerShell에서 먼저 다음을 실행한다. 이 단계는 관리자 권한이 필요하지 않으며, 실제 네트워크 설정을 변경하지 않는다.

```powershell
Set-Location '패키지-루트-경로'
& '.\WifiProfileSwitcher.exe' doctor
& '.\WifiProfileSwitcher.exe' adapters
& '.\WifiProfileSwitcher.exe' validate --config '.\config.example.json'
```

`adapters` 결과에서 어댑터 이름과 GUID를 확인해 개인 메모에만 적는다. 공유 결과에는 GUID·MAC·SSID를 포함하지 않고 성공/실패와 오류 코드만 적는다. `validate`가 실패하면 내부 값을 추측해 고치지 말고 설정 스키마와 관리자에게 받은 값을 다시 확인한다.

예시 설정을 직접 수정하지 말고 로컬 설정 파일을 별도로 만든다. `*.local.json`은 공개 대상에서 제외되지만, 시험이 끝나면 원본을 삭제하거나 보호된 위치로 옮긴다.

```powershell
Copy-Item '.\config.example.json' '.\config.local.json'
notepad '.\config.local.json'
& '.\WifiProfileSwitcher.exe' validate --config '.\config.local.json'
```

`adapterId`, SSID, 고정 IPv4, prefix length, 게이트웨이, DNS를 한 항목씩 실제 시험 PC와 네트워크 관리자 자료로 대조한다. 이 파일을 저장소, 메신저, 이슈에 첨부하지 않는다.

## 설치

관리자 PowerShell을 열고 패키지 루트에서 실행한다. UAC 창이 나타나는 것은 정상이다. 스크립트는 자동으로 권한을 올리지 않는다.

```powershell
Set-Location '패키지-루트-경로'
& '.\scripts\install.ps1' -ConfigPath '.\내부-설정-파일.json'
```

`-ConfigPath`를 생략하면 패키지의 `config.example.json`을 사용한다. 설치 전에 스크립트가 표시하는 변경 목록을 읽고 `INSTALL`을 직접 입력한다. 설치 스크립트는 입력한 설정의 `mode`를 무조건 `observe`로 저장하고, 서비스는 만들지만 바로 시작하지 않는다. 원본 패키지의 EXE를 관리자 권한으로 실행하지 않고 보호된 설치 위치로 복사한 뒤 복사본으로 설정 검증을 수행한다.

설치가 끝난 뒤 서비스 상태와 보호 위치를 확인한다.

```powershell
Get-Service -Name WifiProfileSwitcher
& 'C:\Program Files\WifiProfileSwitcher\WifiProfileSwitcher.exe' status
```

설치 직후에는 상태 파일이 아직 없어서 `status_not_available`이 나올 수 있다. 서비스를 시작하고 첫 관측이 끝난 뒤 다시 확인한다. 경로는 예시이며 실제 사용자명이나 회사 정보가 들어간 경로를 문서와 결과에 복사하지 않는다. 설치 중 ACL, 서비스 경로, 설정 검증에서 실패하면 파일을 임의로 지우거나 `icacls`·실행 정책을 우회하지 않는다. 오류 코드와 실패 단계만 기록해 담당자에게 전달한다.

PowerShell 실행 정책도 확인한다. 먼저 배포자가 제공한 SHA-256과 파일 출처를 확인하고, 승인된 경우 다운로드한 ZIP 파일 하나에만 다음을 실행한 뒤 새 폴더에 압축을 푼다. 전체 폴더를 재귀적으로 해제하지 않는다.

```powershell
Get-FileHash '.\wifi-profile-switcher-win-x64-버전.zip' -Algorithm SHA256
Unblock-File -LiteralPath '.\wifi-profile-switcher-win-x64-버전.zip'
```

스크립트 자체가 `Restricted` 정책으로 차단되고 조직의 허가가 있다면, 관리자 PowerShell에서 해당 실행 한 번에만 `RemoteSigned`를 지정한다. `Bypass`를 사용하지 않고, LocalMachine·CurrentUser 정책이나 GPO를 바꾸지 않는다.

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File '.\scripts\install.ps1' -ConfigPath '.\config.local.json'
```

AllSigned, WDAC, AppLocker, Constrained Language Mode가 적용된 PC는 조직이 승인한 서명·배포를 사용해야 한다. 정책을 우회하지 않는다.

## 관찰 모드 시험

관리자 PowerShell에서 서비스를 수동으로 시작한다.

```powershell
Start-Service -Name WifiProfileSwitcher
& 'C:\Program Files\WifiProfileSwitcher\WifiProfileSwitcher.exe' status
```

다음 순서로 확인한다.

1. 외부망 Wi-Fi에 연결한 뒤 약 15초 동안 기다린다.
2. `status`를 실행하고 상태 코드가 기록되는지 확인한다.
3. 내부망 Wi-Fi로 이동한 뒤 약 15초 동안 기다린다.
4. `status`를 다시 실행한다.
5. `Get-NetIPAddress`, `Get-DnsClientServerAddress`로 대상 어댑터의 주소가 바뀌지 않았는지 확인한다.
6. Wi-Fi를 끊었다가 다시 연결하고, 절전 모드에서 복귀한 뒤에도 관찰이 이어지는지 확인한다.

관찰 모드는 SSID 원문을 공유 상태에 기록하지 않는다. 상태·결과를 공유할 때는 원본 JSON, 예외 전문, `netsh` 원문, SSID, IP, MAC, BSSID를 첨부하지 않는다. 아래처럼 허용된 상태 코드와 수치만 기록한다.

```text
OS: Windows 11 (세부 빌드 생략 가능)
doctor: PASS/FAIL
adapters: PASS/FAIL, 대상 어댑터 수치만 기록
observe 외부망: PASS/FAIL, IP 변경 없음
observe 내부망: PASS/FAIL, IP 변경 없음
재접속: PASS/FAIL
절전 복귀: PASS/FAIL
실패 단계 코드: 예) config_validation_failed
```

자주 확인할 상태 코드와 처리 방향은 다음과 같다.

| 코드 | 확인할 것 | 다음 행동 |
| --- | --- | --- |
| `ssid_unavailable` | 연결 직후인지, SSID 조회 권한·위치 정책인지 | 잠시 기다린 뒤 재확인하고 IP/DNS는 직접 바꾸지 않음 |
| `location_or_policy_denied` | Windows 위치 설정과 조직 정책 | 관리자에게 허용 경로를 문의하고 우회하지 않음 |
| `administrator_required` | 관리자 PowerShell과 승인된 설치 절차 | 관리자에게 요청하거나 시험을 중지 |
| `unsafe_file_owner`, `unsafe_write_permissions`, `unsafe_reparse_path` | 설치·데이터 위치의 소유자, ACL, 정션 | 임의 ACL 변경 없이 기존 설치를 제거하고 담당자 확인 |
| `complex_network_configuration` | 복수 IPv4 주소, 사용자 지정 경로, VPN/가상 NIC | 단순한 시험 Wi-Fi 어댑터에서 다시 시험 |
| `ineffective` | 적용 뒤 실제 IPv4/DNS 재조회 결과 | 서비스 중지 후 복구 명령과 Windows 설정 확인 |
| `failure_limit` | 반복 적용 실패나 다른 네트워크 관리 도구 | 반복 재시작하지 말고 복구·정책 확인 |
| `wifi_access_denied`, `policy_or_access_denied` | WLAN·NetTCPIP·PowerShell 정책 | 조직 승인 여부를 확인하고 우회하지 않음 |

`hold` fallback은 연결·SSID·정책을 확실히 판정하지 못할 때 현재 설정을 유지하는 선택이다. 해당 상황에서 DHCP로 바꾸는 설정은 네트워크 관리자의 승인 없이 사용하지 않는다.

## 프로토타입의 한계

SSID를 읽고 Windows 네트워크 명령을 실행하는 사이에 연결이 바뀌는 것을 운영체제가 하나의 원자 작업으로 보장하지 않는다. 프로그램은 적용 직전에 다시 SSID를 확인하고 다음 관측에서 보정하지만, 처음 시험에서는 Wi-Fi를 빠르게 번갈아 연결하지 않는다. 전환 중 연결이 안정된 상태에서 각 망을 충분히 유지한다.

`snapshot.json`은 첫 적용 당시의 단순한 IPv4 주소·prefix·기본 게이트웨이·DNS 상태를 보관한다. 고급 경로, 메트릭, VPN, 프록시, IPv6, 방화벽 정책까지 완전히 복원하는 백업으로 간주하지 않는다. 이런 구성이 있는 어댑터는 시험에서 제외하고 담당자와 별도 복구 절차를 정한다.

## 설정 교체

새 개인 설정 파일을 준비한 뒤 관리자 PowerShell에서 설정 스크립트를 실행한다. `-Enable` 없이 실행하면 항상 observe로 저장된다.

```powershell
& 'C:\Program Files\WifiProfileSwitcher\scripts\configure.ps1' `
  -ConfigPath '시험 PC의 안전한 경로\config.json'
```

변경 목록에서 `CONFIGURE`를 입력한다. 실행 중인 서비스는 잠시 중지되고, 검증 성공 뒤에만 이전 실행 상태로 돌아온다. 검증·교체·재시작 중 하나라도 실패하면 이전 설정을 복원하고 서비스를 자동으로 시작하지 않는다.

## enforce 시험

다음 조건을 모두 확인한 뒤에만 수행한다.

- 대상 GUID가 실제 Wi-Fi 어댑터 하나와 일치한다.
- 고정 IPv4, prefix length, 게이트웨이, DNS는 네트워크 관리자에게 받은 값이며 저장소와 공유 결과에 남기지 않는다.
- DHCP 복귀가 가능한 관리자 PowerShell을 로컬에서 열 수 있다.
- 업무용 연결이 아닌 격리된 시험 네트워크를 사용한다.
- 회사 보안 정책이 해당 EXE·서비스·네트워크 변경을 허용한다.

```powershell
& 'C:\Program Files\WifiProfileSwitcher\scripts\configure.ps1' `
  -ConfigPath '시험 PC의 안전한 경로\config.json' -Enable
```

`ENABLE`을 정확히 입력해야 enforce 설정이 저장된다. 이후 서비스가 실행 중이면 다음 관측부터 대상 어댑터에만 적용을 시도한다. SSID 미확인, 중복 프로필, 다른 GUID, 적용 후 재조회 실패는 보류되어야 한다. IP가 바뀌지 않거나 잘못된 경우 반복해서 재시작하지 말고 즉시 복구 절차로 이동한다.

enforce 시험은 아래 순서로 나누어 기록한다.

1. 고정 프로필에서 내부망 SSID를 연결하고 IPv4·게이트웨이·DNS가 기대한 상태인지 Windows 설정으로 확인한다.
2. 외부망 SSID로 이동해 DHCP와 자동 DNS가 적용되는지 확인한다.
3. DHCP 프로필을 별도로 사용한다면 내부망·외부망 각각의 DNS 상태를 확인한다.
4. Wi-Fi 재접속, 재부팅, 절전 복귀를 각각 한 번씩 시험한다.
5. 등록되지 않은 SSID와 SSID 조회 실패에서는 IP/DNS가 바뀌지 않는지 확인한다.
6. 서비스가 SSID를 읽지 못하면 서비스가 반복 실패하지 않는지 확인하고, 관리자 터미널에서 `& '.\WifiProfileSwitcher.exe' watch --config '.\config.local.json'`를 observe 모드로 실행해 사용자 세션 차이를 비교한다. 이 foreground 시험은 서비스 대체 세션을 자동 등록하지 않는다.
7. 회사 도메인 정책·WDAC·AppLocker가 서비스 또는 PowerShell을 막으면 `hold` 정책과 허용된 조직 배포 경로를 확인한다. 정책을 우회하지 않는다.

## 복구와 제거

네트워크가 예상과 다르면 서비스부터 중지한다. 복구 명령은 반드시 명시적 확인 인자를 요구한다.

```powershell
Stop-Service -Name WifiProfileSwitcher
& 'C:\Program Files\WifiProfileSwitcher\WifiProfileSwitcher.exe' recover-dhcp --confirm
```

최초 백업을 복원해야 하는 시험이라면 다음을 사용한다.

```powershell
& 'C:\Program Files\WifiProfileSwitcher\WifiProfileSwitcher.exe' restore --confirm
```

두 명령은 `RECOVER`를 한 번 더 입력해야 하며, 결과에 `recovery_command_completed`와 DHCP·자동 DNS 여부가 표시된다. `status`는 중지된 서비스의 이전 상태를 보여줄 수 있으므로 복구 자체의 증거로 사용하지 않는다. Windows 네트워크 설정에서 대상 어댑터의 실제 주소와 DNS를 직접 확인한다. 두 명령이 실패하면 네트워크 관리자나 Windows 설정에서 대상 어댑터만 복구한다. 방화벽, VPN, IPv6, 프록시 설정은 이 프로그램의 복구 범위가 아니다.

제거 전에 현재 IP/DNS를 유지할지 복구할지 결정한다. 제거 스크립트는 현재 네트워크 설정을 변경하지 않는다.

```powershell
& 'C:\Program Files\WifiProfileSwitcher\scripts\uninstall.ps1'
```

변경 목록을 읽고 `UNINSTALL`을 입력해야 서비스와 보호 디렉터리를 제거한다. 재분석 지점이나 예상 밖 ACL이 발견되면 안전을 위해 중단한다. 그 경우 심볼릭 링크를 지우거나 `-Force`로 우회하지 않는다.
