# 001: Windows 시험용 프로토타입

## 범위

연결된 SSID에 따라 고정 IPv4/DNS 또는 DHCP/자동 DNS로 바꾼다.
Wi-Fi 접속 대상 선택, 비밀번호 관리, VPN/프록시/IPv6 변경은 제외한다.
Windows 11 Wi-Fi PC에서 최초 시험한다. macOS에서는 Core 테스트, 크로스 빌드와 정적 검사를 수행한다.

## 실행 방식

C#/.NET 10 서비스, Windows API로 연결 SSID 조회. 기본 관찰 모드.
서비스 계정 조회가 제한되면 관리자 터미널에서 foreground watch를 실행해 사용자 세션 대안을 검증한다.
초기 프로토타입은 대체 세션 프로세스를 자동 등록하지 않는다.
5초 폴링 및 연속 관측 안정화로 재접속/절전 복귀를 처리한다. 단순하고 시험하기 쉬운 경로를 우선한다.
서비스 설치는 LocalSystem, 보호된 Program Files/ProgramData, 실제 변경은 enforce 명시 설정 이후.

## 계약

실행 파일: WifiProfileSwitcher.exe
서비스: WifiProfileSwitcher
설치 위치: Program Files/WifiProfileSwitcher
설정/상태/백업: ProgramData/WifiProfileSwitcher
명령: doctor, adapters, validate --config PATH, observe --config PATH,
watch [--config PATH], service, status, recover-dhcp --confirm,
restore --confirm. 설치/설정/제거는 scripts/*.ps1가 담당한다.

설정 필드: version=1, adapterId=GUID, mode=observe|enforce,
fallback=dhcp|hold, pollSeconds=5, profiles=[{id,ssids,mode=static|dhcp,address,prefixLength,gateway,dnsServers}].
설정은 camelCase JSON. 사내 프로필 없이 enforce를 켤 수 없다.

## 완료 조건

- [x] 순수 판정/검증과 실패·중복 방지 테스트
- [x] SSID 조회, 명령 실행, 관찰/전환 서비스 및 진단
- [x] 관리자 설치·관찰 기본값·명시적 활성화·제거·복구
- [x] 소스/패키지 개인정보 검사와 Windows CI 빌드
- [x] 동료용 시험 절차, 개인정보 없는 결과 양식
- [ ] Windows 실기 결과는 별도 기록 (이번 Mac 환경에서 미검증)

## 적용 안전성

대상 GUID만 변경한다. SSID 미확인·충돌 프로필·정책 제한은 보류한다.
명령/프로필 문자열을 셸 코드로 결합하지 않는다. 최초 설정은 로컬에 백업한다.
고정 IP와 DNS는 별도로 반영되므로 적용 후 재조회한다. 부분 실패는 즉시 자동 반복하지 않고
실패 상한 및 backoff를 적용한다. 다른 네트워크로 이동한 뒤 이전 고정 IP를 자동 복원하지 않는다.
복구는 서비스 중지 후 명시적 승인으로 DHCP 복귀 또는 최초 백업 복원이다.
