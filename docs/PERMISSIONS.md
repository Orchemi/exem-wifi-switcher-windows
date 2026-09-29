# Windows 권한과 보안 경계

이 문서는 시험용 프로토타입이 요청하는 권한과 요청하지 않는 권한을 정리한다. Windows 실기에서 아직 확인하지 않은 내용은 구현 가정이며, 동료 시험 결과로 갱신한다.

## 권한 모델

- 설치·설정 변경·제거는 관리자 PowerShell에서 직접 실행한다. 스크립트가 UAC를 자동 호출하거나 숨은 프로세스를 만들지 않는다.
- 서비스는 `LocalSystem` 계정으로 등록하고 자동(지연 시작)으로 설정한다. 서비스는 설치 직후 자동으로 시작하지 않는다.
- 서비스 실행 파일과 런타임 스크립트는 `Program Files\WifiProfileSwitcher`에 둔다.
- 설정·상태·백업은 `ProgramData\WifiProfileSwitcher`에 둔다. 일반 사용자는 이 디렉터리에 쓰거나 삭제할 수 없다.
- 설치 위치와 데이터 위치의 소유자는 `SYSTEM` 또는 기본 제공 Administrators SID여야 한다. ACL 상속은 차단하고 SYSTEM·Administrators에만 전체 제어를 준다.
- 설치 디렉터리의 일반 사용자는 읽기·실행만 가능하다. 데이터 디렉터리에는 일반 사용자 ACL을 주지 않는다.
- 서비스 경로와 대상 파일에 재분석 지점(심볼릭 링크·정션)이 있으면 설치·설정·제거를 중단한다.

스크립트는 `icacls.exe`와 Windows 서비스 API를 사용한다. 설치·서비스 실행이 Windows PowerShell 기본 `Restricted` 정책에 막히면 조직의 허가를 받은 경우에만 해당 실행 프로세스에 `-ExecutionPolicy RemoteSigned`를 지정할 수 있다. `Bypass`를 사용하지 않으며 LocalMachine·CurrentUser 정책과 GPO를 바꾸지 않는다. 런타임이 시작하는 PowerShell도 `NoProfile`과 승인된 `RemoteSigned` 범위로 제한하며, 회사의 WDAC, AppLocker, Controlled Folder Access, MDM, PowerShell Constrained Language Mode가 막으면 정책 담당자가 승인한 서명·배포 방법을 사용해야 한다. 정책 우회 방법은 제공하지 않는다.

## 설치 시 변경

`install.ps1`은 다음 순서로 움직인다.

1. Windows와 관리자 권한을 확인한다.
2. 기존 설치 디렉터리·서비스가 있으면 덮어쓰지 않고 중단한다.
3. 패키지 파일의 경로와 모든 상위 경로에 재분석 지점이 없는지 검사한다.
4. 새 설치·데이터 디렉터리를 만들고 ACL을 보호한다.
5. 실행 파일과 런타임 파일을 보호된 설치 위치로 복사한다.
6. 설정을 `observe`로 강제해 데이터 위치에 저장하고, 보호된 복사본 EXE로 검증한다.
7. 검증 성공 후에만 `LocalSystem` 서비스와 지연 자동 시작을 등록한다.

실패 시 이번 실행에서 새로 만든 서비스와 디렉터리만 정리한다. 기존 설치를 수정하지 않으므로 기존 설치가 발견되면 제거 후 재설치하지 말고 담당자의 확인을 받는다. 패키지 출처와 EXE 신뢰성은 이 스크립트가 대신 판단할 수 없다. 동료는 배포 전 해시·서명·조직 배포 경로를 확인해야 한다.

## 설정 변경 시 변경

`configure.ps1`은 설치된 EXE와 보호된 데이터 위치를 확인한 뒤 입력 JSON을 읽는다. `-Enable`이 없으면 입력에 `enforce`가 있어도 `observe`로 바꾼다. `-Enable`을 사용하면 변경 목록을 표시하고 `ENABLE`을 정확히 입력해야 `enforce`로 저장한다.

`ENABLE` 확인 화면에는 대상 `adapterId`, 각 프로필의 SSID·mode, 고정 프로필의 IPv4/prefix·게이트웨이·DNS가 표시된다. 이 값은 로컬 검토용이며 로그나 공유 결과에 복사하지 않는다.

설정 교체는 다음 안전 조건을 가진다.

- 새 설정을 임시 파일에 쓴 뒤 설치된 EXE로 먼저 검증한다.
- `snapshot.json`이 있고 그 안의 `adapterId`가 새 설정과 다르면 복구 안전을 위해 거부한다.
- 실행 중인 서비스는 교체 동안 중지한다.
- 기존 `config.json`은 원자 교체용 백업으로 보존하고, 교체 후 다시 검증한다.
- 검증 또는 서비스 재시작에 실패하면 이전 설정을 복원한다.
- 실패 경로에서는 서비스를 자동으로 다시 시작하지 않는다.

서비스가 실행되지 않는 상태에서는 설정만 저장하고 서비스를 시작하지 않는다. enforce 활성화는 관리자와 동료가 대상 GUID·프로필·복구 수단을 확인한 뒤 직접 실행해야 한다.

## 제거 시 변경

`uninstall.ps1`은 서비스 실행 경로가 예상한 설치 EXE인지 확인한다. 같은 서비스 이름이 다른 파일을 가리키거나, 대상에 재분석 지점·예상 밖 ACL이 있으면 제거를 거부한다. 확인 후 서비스 중지·등록 해제·보호된 설치 디렉터리·데이터 디렉터리 삭제를 수행한다.

제거는 현재 어댑터의 IP·DNS를 DHCP로 되돌리지 않는다. 제거 전에 다음 중 하나를 관리자가 명시적으로 수행해야 한다.

```powershell
& 'C:\Program Files\WifiProfileSwitcher\WifiProfileSwitcher.exe' recover-dhcp --confirm
& 'C:\Program Files\WifiProfileSwitcher\WifiProfileSwitcher.exe' restore --confirm
```

두 명령은 서비스가 중지된 상태에서 실행한다. 복구 실패 시 네트워크 관리자나 Windows 설정에서 대상 Wi-Fi 어댑터만 복원한다.

## 시험에서 확인할 항목

- 일반 사용자로 설치·설정·제거를 실행했을 때 관리자 안내와 함께 거부되는가.
- 일반 사용자가 설치 EXE와 스크립트를 읽을 수 있지만 ProgramData 설정을 수정·삭제할 수 없는가.
- 보호 위치에 심볼릭 링크·정션을 넣었을 때 모든 변경·제거가 중단되는가.
- 회사 정책이 EXE, 서비스, SSID 조회, IP 변경을 차단할 때 오류가 정책 우회 없이 드러나는가.
- 서비스가 조회하는 SSID와 대상 어댑터가 사용자 세션/위치 권한 정책에서 허용되는가.
- 서비스가 실행되지 않는 경우 foreground `watch`를 관리자가 직접 시험할 수 있는가.

이 저장소에서 macOS만으로 위 Windows 동작을 검증했다고 표시하지 않는다. 동료의 실기 결과는 SSID·IP·MAC·예외 원문을 제거한 상태 코드와 재현 단계로만 공유한다.

실행 정책과 다운로드 파일 차단 해제의 기준은 [PowerShell 실행 정책 문서](https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_execution_policies)와 [Unblock-File 문서](https://learn.microsoft.com/powershell/module/microsoft.powershell.utility/unblock-file)를 따른다.
