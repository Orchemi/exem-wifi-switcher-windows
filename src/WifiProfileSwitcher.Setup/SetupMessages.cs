using WifiProfileSwitcher.Core;
using WifiProfileSwitcher.Windows;

namespace WifiProfileSwitcher.Setup;

internal static class SetupMessages
{
    public static string Code(Exception? error) => error switch
    {
        ManualInputException manual => manual.Code,
        SafeException safe => safe.Code,
        CaptureException capture => capture.Code,
        ConfigException => "config_invalid",
        UnauthorizedAccessException => "access_denied",
        _ => "setup_failed"
    };

    public static string StartFailure(Exception error) => Code(error) switch
    {
        "ssid_not_configured" => "회사 Wi-Fi에 연결한 뒤 ‘저장하고 시작’을 다시 누르세요.",
        "service_observation_failed" or "location_or_policy_denied" or "wifi_access_denied" or "ssid_unavailable" or "ssid_api_unavailable" or "wifi_disconnected"
            => "Wi-Fi를 확인하지 못해 자동 전환을 시작하지 못했습니다. 연결·권한을 확인한 뒤 다시 시도하세요.",
        _ => "자동 전환을 시작하지 못했습니다. ‘더 보기’에서 오류를 확인한 뒤 다시 시도하세요."
    };

    public static string Short(Exception error) => Code(error) switch
    {
        "manual_ssid_required" => "회사 Wi-Fi 이름을 입력해 주세요.",
        "manual_ssid_invalid" or "manual_ssid_too_long" => "Wi-Fi 이름을 확인해 주세요. 최대 32바이트입니다.",
        "manual_address_required" or "manual_address_invalid" => "사용할 고정 IPv4 주소를 입력해 주세요.",
        "manual_subnet_mask_required" or "manual_subnet_mask_invalid" => "서브넷 마스크를 확인해 주세요. 예: 255.255.255.0",
        "manual_gateway_required" or "manual_gateway_invalid" => "게이트웨이 IPv4 주소를 입력해 주세요.",
        "manual_gateway_subnet_mismatch" => "게이트웨이는 IP 주소와 같은 서브넷이어야 합니다.",
        "manual_gateway_same_as_address" => "게이트웨이와 IP 주소는 달라야 합니다.",
        "manual_dns_required" or "manual_dns_invalid" => "DNS IPv4 주소를 입력해 주세요. 여러 개는 쉼표로 구분합니다.",
        "manual_dns_duplicate" => "중복된 DNS 주소를 지워 주세요.",
        "manual_dns_too_many" => "DNS 서버는 최대 4개까지 입력할 수 있습니다.",
        "select_wifi_adapter" or "manual_adapter_id_required" => "사용할 Wi-Fi 어댑터를 선택해 주세요.",
        "editor_complex_config" => "여러 프로필이 있는 설정은 이 창에서 편집할 수 없습니다.",
        "service_observation_failed" => "Wi-Fi 감지를 확인하지 못했습니다. ‘더 보기’에서 오류를 확인하세요.",
        _ => "작업을 완료하지 못했습니다. ‘더 보기’에서 오류를 확인하세요."
    };

    public static string For(Exception error)
    {
        var code = Code(error);
        if (error is ManualInputException) return Short(error);
        return code switch
        {
            "editor_complex_config" => "이 창은 회사 Wi-Fi 하나의 고정 IP 설정만 편집합니다. 기존 여러 프로필은 보존했습니다. 기존 설정 도구를 이용하세요.",
            "adapter_change_not_supported" => "기존 설치의 Wi-Fi 어댑터는 바꿀 수 없습니다. 자동 전환 서비스를 중지했습니다. 기존 어댑터를 선택해 다시 저장하세요.",
            "ssid_not_configured" => "입력한 회사 Wi-Fi에 연결한 뒤 자동 전환을 켜세요. 현재 IP가 DHCP여도 됩니다.",
            "wifi_enumeration_failed" or "adapter_missing" => "Wi-Fi 어댑터를 찾지 못했습니다. 장치가 켜져 있는지 확인하고 창을 다시 여세요.",
            "wifi_disconnected" => "연결된 Wi-Fi가 없습니다. 고정 IP가 설정된 회사 Wi-Fi에 연결한 뒤 다시 감지하세요.",
            "multiple_connected_wifi" => "Wi-Fi가 여러 개 연결되어 있습니다. 사용할 회사 Wi-Fi 하나만 연결한 뒤 다시 감지하세요.",
            "capture_dhcp_enabled" => "현재 IP는 DHCP로 자동 할당되었습니다. 임시 주소를 고정 IP로 저장하지 않습니다. 회사의 고정 IP 설정이 된 PC에서 다시 시도하세요.",
            "capture_dns_automatic" => "DNS가 자동 설정입니다. 이 시험판은 고정 IP와 수동 DNS가 모두 설정된 회사 Wi-Fi에서 가져올 수 있습니다.",
            "connection_changed" or "config_changed" => "확인하는 동안 Wi-Fi 또는 설정이 바뀌었습니다. 설치를 진행하지 않았습니다. 현재 연결과 설정을 다시 확인하세요.",
            "service_observation_failed" => "백그라운드에서 현재 설정을 확인하지 못해 자동 전환을 켜지 않았습니다. 회사 Wi-Fi 연결과 위치 권한, 회사 보안 정책을 확인하세요. 다른 프로그램이 설정을 관리하는지도 확인하세요.",
            "already_installed" => "기존 설치가 있습니다. 이 시험판은 기존 설치를 덮어쓰지 않습니다.",
            "setup_already_running" => "설치 창이 이미 열려 있습니다. 기존 창에서 계속하세요.",
            "setup_payload_missing" or "setup_payload_invalid" => "설치 파일을 읽을 수 없습니다. 완성된 Setup.exe를 다시 내려받으세요.",
            "administrator_required" or "access_denied" => "관리자 승인이 필요합니다. 회사에서 제한한 PC라면 관리자에게 설치 승인을 요청하세요.",
            "location_or_policy_denied" or "wifi_access_denied" or "ssid_unavailable" or "ssid_api_unavailable" => "Wi-Fi 이름을 읽을 수 없습니다. Windows의 위치 권한과 회사 보안 정책을 확인한 뒤 다시 시도하세요.",
            "powershell_blocked_or_invalid_reply" or "powershell_language_restricted" or "setup_command_failed" => "Windows 또는 회사 정책으로 작업을 완료하지 못했습니다. PowerShell 실행 정책·앱 실행 제한을 관리자에게 확인하세요. 보안 정책을 해제하지 마세요. 일부 설치 단계가 완료되었을 수 있으므로 창을 다시 열어 상태를 확인하세요.",
            "complex_network_configuration" => "복수 IP나 사용자 지정 경로가 있는 설정은 자동으로 가져오지 않습니다. 네트워크 관리자에게 구성을 확인하세요.",
            _ => "설정을 안전하게 확인하지 못해 작업을 완료하지 않았습니다. 회사 Wi-Fi에 고정 IP·게이트웨이·수동 DNS가 정상적으로 설정되어 있는지 확인하세요. 기존 설치가 있다면 설정·권한도 확인하세요."
        };
    }
}
