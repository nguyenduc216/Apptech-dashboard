using System.Globalization;
using System.Text;

namespace ApptechDashboard.Models;

public class NavigationMenuItem
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string? ParentCode { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Url { get; set; }
    public string? CssClass { get; set; }
    public string? SortOrder { get; set; }
    public List<NavigationMenuItem> Children { get; set; } = [];

    public string IconClass
    {
        get
        {
            var forced = MenuIconMap.ResolveForced(Code, Title, Url);
            if (!string.IsNullOrWhiteSpace(forced))
            {
                return forced;
            }

            var normalized = MenuIconMap.NormalizeCssClass(CssClass);
            return !string.IsNullOrWhiteSpace(normalized)
                ? normalized
                : MenuIconMap.Resolve(Code, Title, Url);
        }
    }

    public bool HasChildren => Children.Count > 0;
}

internal static class MenuIconMap
{
    public static string? ResolveForced(string code, string title, string? url = null)
    {
        var normalizedCode = NormalizeText(code);
        var normalizedTitle = NormalizeText(title);
        var normalizedUrl = NormalizeText(url);

        return normalizedCode switch
        {
            "report" => "fa-solid fa-chart-column",
            "report_chamcong" => "fa-solid fa-calendar-check",
            "report-chamcong" => "fa-solid fa-calendar-check",
            "baocao" => "fa-solid fa-chart-column",
            "bao_cao" => "fa-solid fa-chart-column",
            "bao-cao" => "fa-solid fa-chart-column",
            "ncc" => "fa-solid fa-handshake",
            "nhacungcap" => "fa-solid fa-handshake",
            "nha_cung_cap" => "fa-solid fa-handshake",
            "nha-cung-cap" => "fa-solid fa-handshake",
            "nhapkho" => "fa-solid fa-dolly",
            "nhap_kho" => "fa-solid fa-dolly",
            "nhap-kho" => "fa-solid fa-dolly",
            "xuatkho" => "fa-solid fa-truck-ramp-box",
            "xuat_kho" => "fa-solid fa-truck-ramp-box",
            "xuat-kho" => "fa-solid fa-truck-ramp-box",
            "setting" => "fa-solid fa-sliders",
            "settings" => "fa-solid fa-sliders",
            "cai_dat" => "fa-solid fa-sliders",
            "cai-dat" => "fa-solid fa-sliders",
            "material" => "fa-solid fa-toolbox",
            "vat_tu" => "fa-solid fa-toolbox",
            "vat-tu" => "fa-solid fa-toolbox",
            "chi_tiet_vat_tu" => "fa-solid fa-toolbox",
            _ when ContainsAny(normalizedTitle, "ncc", "nha cung cap") => "fa-solid fa-handshake",
            _ when ContainsAny(normalizedTitle, "nhap kho") => "fa-solid fa-dolly",
            _ when ContainsAny(normalizedTitle, "xuat kho") => "fa-solid fa-truck-ramp-box",
            _ when ContainsAny(normalizedTitle, "cai dat", "setting", "settings") => "fa-solid fa-sliders",
            _ when ContainsAny(normalizedTitle, "vat tu") => "fa-solid fa-toolbox",
            _ when ContainsAny(normalizedUrl, "ncc", "nhacungcap", "nha-cung-cap", "nha_cung_cap") => "fa-solid fa-handshake",
            _ when ContainsAny(normalizedUrl, "nhapkho", "nhap-kho", "nhap_kho") => "fa-solid fa-dolly",
            _ when ContainsAny(normalizedUrl, "xuatkho", "xuat-kho", "xuat_kho") => "fa-solid fa-truck-ramp-box",
            _ when ContainsAny(normalizedUrl, "setting", "settings", "cai-dat", "cai_dat") => "fa-solid fa-sliders",
            _ when ContainsAny(normalizedUrl, "vat-tu") => "fa-solid fa-toolbox",
            _ => null
        };
    }

    public static string? NormalizeCssClass(string? cssClass)
    {
        if (string.IsNullOrWhiteSpace(cssClass))
        {
            return null;
        }

        var normalized = cssClass.Trim() switch
        {
            "fa fa-dashboard" => "fa-solid fa-gauge-high",
            "fa fa-th-list" => "fa-solid fa-layer-group",
            "fa fa-user" => "fa-solid fa-user",
            "fa fa-users" => "fa-solid fa-users",
            "fa fa-paper-plane" => "fa-solid fa-paper-plane",
            "fa fa-history" => "fa-solid fa-clock-rotate-left",
            "fa fa-calendar" => "fa-solid fa-calendar-days",
            "fa fa-caret-right" => "fa-solid fa-angle-right",
            var value when value.StartsWith("fa ") => value.Replace("fa ", "fa-solid ", StringComparison.Ordinal),
            var value => value
        };

        return LooksLikeFontAwesomeClass(normalized) && !IsGenericNavigationIcon(normalized)
            ? normalized
            : null;
    }

    public static string Resolve(string code, string title, string? url = null)
    {
        var normalizedCode = NormalizeText(code);
        var normalizedTitle = NormalizeText(title);
        var normalizedUrl = NormalizeText(url);

        return normalizedCode switch
        {
            "dashboard" => "fa-solid fa-gauge-high",
            "category" => "fa-solid fa-layer-group",
            "kythuat" => "fa-solid fa-screwdriver-wrench",
            "qrcode" => "fa-solid fa-qrcode",
            "qr_code" => "fa-solid fa-qrcode",
            "qr-code" => "fa-solid fa-qrcode",
            "user_management" => "fa-solid fa-user-gear",
            "user_role" => "fa-solid fa-user-shield",
            "customer" => "fa-solid fa-address-book",
            "request" => "fa-solid fa-clipboard-list",
            "request_history" => "fa-solid fa-magnifying-glass",
            "checkin" => "fa-solid fa-calendar-check",
            "report" => "fa-solid fa-chart-column",
            "report_chamcong" => "fa-solid fa-calendar-check",
            "report-chamcong" => "fa-solid fa-calendar-check",
            "baocao" => "fa-solid fa-chart-column",
            "bao_cao" => "fa-solid fa-chart-column",
            "bao-cao" => "fa-solid fa-chart-column",
            "cat_don_vi_tinh" => "fa-solid fa-ruler-combined",
            "cat_phong_ban" => "fa-solid fa-building-user",
            "cat_nhan_vien" => "fa-solid fa-id-card-clip",
            "cat_cong_viec" => "fa-solid fa-list-check",
            "ncc" => "fa-solid fa-handshake",
            "nhacungcap" => "fa-solid fa-handshake",
            "nha_cung_cap" => "fa-solid fa-handshake",
            "nha-cung-cap" => "fa-solid fa-handshake",
            "material" => "fa-solid fa-toolbox",
            "vat_tu" => "fa-solid fa-toolbox",
            "vat-tu" => "fa-solid fa-toolbox",
            "chi_tiet_vat_tu" => "fa-solid fa-toolbox",
            "nhapkho" => "fa-solid fa-dolly",
            "nhap_kho" => "fa-solid fa-dolly",
            "nhap-kho" => "fa-solid fa-dolly",
            "xuatkho" => "fa-solid fa-truck-ramp-box",
            "xuat_kho" => "fa-solid fa-truck-ramp-box",
            "xuat-kho" => "fa-solid fa-truck-ramp-box",
            "setting" => "fa-solid fa-sliders",
            "settings" => "fa-solid fa-sliders",
            "cai_dat" => "fa-solid fa-sliders",
            "cai-dat" => "fa-solid fa-sliders",
            _ when ContainsAny(normalizedTitle, "quan ly chung", "tong quan he thong", "tong hop") => "fa-solid fa-sliders",
            _ when ContainsAny(normalizedTitle, "quan ly nguoi dung", "nguoi dung", "tai khoan nguoi dung") => "fa-solid fa-user-gear",
            _ when ContainsAny(normalizedTitle, "vai tro nguoi dung", "vai tro") => "fa-solid fa-user-shield",
            _ when ContainsAny(normalizedTitle, "quan ly danh muc", "danh muc", "nhom danh muc") => "fa-solid fa-layer-group",
            _ when ContainsAny(normalizedTitle, "ky thuat") => "fa-solid fa-screwdriver-wrench",
            _ when ContainsAny(normalizedTitle, "quan ly khach hang", "khach hang") => "fa-solid fa-address-book",
            _ when ContainsAny(normalizedTitle, "quan ly yeu cau", "gui yeu cau") => "fa-solid fa-paper-plane",
            _ when ContainsAny(normalizedTitle, "tra cuu yeu cau", "lich su yeu cau") => "fa-solid fa-magnifying-glass",
            _ when ContainsAny(normalizedTitle, "tra cuu cham cong") => "fa-solid fa-calendar-check",
            _ when ContainsAny(normalizedTitle, "danh sach nhan vien") => "fa-solid fa-id-card-clip",
            _ when ContainsAny(normalizedTitle, "don vi tinh") => "fa-solid fa-ruler-combined",
            _ when ContainsAny(normalizedTitle, "phong ban") => "fa-solid fa-building-user",
            _ when ContainsAny(normalizedTitle, "cong viec") => "fa-solid fa-list-check",
            _ when ContainsAny(normalizedTitle, "ncc", "nha cung cap") => "fa-solid fa-handshake",
            _ when ContainsAny(normalizedTitle, "hang hoa") => "fa-solid fa-boxes-stacked",
            _ when ContainsAny(normalizedTitle, "vat tu") => "fa-solid fa-toolbox",
            _ when ContainsAny(normalizedTitle, "nhap kho") => "fa-solid fa-dolly",
            _ when ContainsAny(normalizedTitle, "xuat kho") => "fa-solid fa-truck-ramp-box",
            _ when ContainsAny(normalizedTitle, "qrcode", "qr code", "ma qr", "ma qrcode") => "fa-solid fa-qrcode",
            _ when ContainsAny(normalizedTitle, "dashboard", "trang chu", "tong quan") => "fa-solid fa-gauge-high",
            _ when ContainsAny(normalizedTitle, "nhan vien") => "fa-solid fa-id-badge",
            _ when ContainsAny(normalizedTitle, "user", "tai khoan") => "fa-solid fa-user",
            _ when ContainsAny(normalizedTitle, "category", "nhom", "loai") => "fa-solid fa-boxes-stacked",
            _ when ContainsAny(normalizedTitle, "yeu cau", "request") => "fa-solid fa-clipboard-list",
            _ when ContainsAny(normalizedTitle, "lich su", "history", "tra cuu") => "fa-solid fa-clock-rotate-left",
            _ when ContainsAny(normalizedTitle, "cham cong", "checkin", "calendar", "lich") => "fa-solid fa-calendar-days",
            _ when ContainsAny(normalizedTitle, "vai tro", "role", "phan quyen", "permission") => "fa-solid fa-user-shield",
            _ when ContainsAny(normalizedTitle, "thiet bi", "device") => "fa-solid fa-microchip",
            _ when ContainsAny(normalizedTitle, "cai dat", "setting", "settings", "config", "he thong") => "fa-solid fa-sliders",
            _ when ContainsAny(normalizedTitle, "bao cao", "report", "thong ke") => "fa-solid fa-chart-column",
            _ when ContainsAny(normalizedTitle, "thong bao", "notify", "notification") => "fa-solid fa-bell",
            _ when ContainsAny(normalizedUrl, "dashboard", "home", "index") => "fa-solid fa-gauge-high",
            _ when ContainsAny(normalizedUrl, "user", "account") => "fa-solid fa-user-gear",
            _ when ContainsAny(normalizedUrl, "role", "permission") => "fa-solid fa-user-shield",
            _ when ContainsAny(normalizedUrl, "khach-hang", "customer", "client") => "fa-solid fa-address-book",
            _ when ContainsAny(normalizedUrl, "cong-viec") => "fa-solid fa-list-check",
            _ when ContainsAny(normalizedUrl, "ncc", "nhacungcap", "nha-cung-cap", "nha_cung_cap") => "fa-solid fa-handshake",
            _ when ContainsAny(normalizedUrl, "hang-hoa") => "fa-solid fa-boxes-stacked",
            _ when ContainsAny(normalizedUrl, "vat-tu") => "fa-solid fa-toolbox",
            _ when ContainsAny(normalizedUrl, "nhapkho", "nhap-kho", "nhap_kho") => "fa-solid fa-dolly",
            _ when ContainsAny(normalizedUrl, "xuatkho", "xuat-kho", "xuat_kho") => "fa-solid fa-truck-ramp-box",
            _ when ContainsAny(normalizedUrl, "request/search", "request/lookup", "requesthistory", "request-history") => "fa-solid fa-magnifying-glass",
            _ when ContainsAny(normalizedUrl, "request") => "fa-solid fa-clipboard-list",
            _ when ContainsAny(normalizedUrl, "history", "log") => "fa-solid fa-clock-rotate-left",
            _ when ContainsAny(normalizedUrl, "checkin", "calendar", "attendance") => "fa-solid fa-calendar-check",
            _ when ContainsAny(normalizedUrl, "qrcode", "qr-code", "qr_code") => "fa-solid fa-qrcode",
            _ when ContainsAny(normalizedUrl, "setting", "settings", "cai-dat", "cai_dat") => "fa-solid fa-sliders",
            _ => "fa-solid fa-grid-2"
        };
    }

    private static bool ContainsAny(string? source, params string[] values)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        return values.Any(value => source.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeFontAwesomeClass(string cssClass)
    {
        if (string.IsNullOrWhiteSpace(cssClass))
        {
            return false;
        }

        return cssClass
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(part => part.StartsWith("fa-", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsGenericNavigationIcon(string cssClass)
    {
        if (string.IsNullOrWhiteSpace(cssClass))
        {
            return false;
        }

        return ContainsAny(
            cssClass,
            "fa-angle-right",
            "fa-angle-left",
            "fa-angle-up",
            "fa-angle-down",
            "fa-caret-right",
            "fa-caret-left",
            "fa-caret-up",
            "fa-caret-down",
            "fa-chevron-right",
            "fa-chevron-left",
            "fa-chevron-up",
            "fa-chevron-down",
            "fa-arrow-right",
            "fa-arrow-left",
            "fa-folder-open",
            "fa-folder");
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder
            .ToString()
            .Replace('đ', 'd')
            .Replace('Đ', 'D')
            .Normalize(NormalizationForm.FormC)
            .ToLowerInvariant();
    }
}
