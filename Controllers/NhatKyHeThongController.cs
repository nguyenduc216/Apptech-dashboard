using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NhatKyHeThongController(SystemAuditService audit, WorkProgressService permissions, ILogger<NhatKyHeThongController> logger) : Controller
{
    [HttpGet("/nhat-ky-he-thong")]
    [HttpGet("/NhatKyHeThong")]
    public async Task<IActionResult> Index(SystemAuditQuery query, CancellationToken ct)
    {
        if (!await permissions.Allowed(User, "NhatKyHeThong_View", ct)) return Forbid();
        if (query.From > query.To) ModelState.AddModelError("", "Thời gian kết thúc không được trước thời gian bắt đầu.");
        if (query.Filters.Count > SystemAuditModel.Labels.Count || query.Filters.Any(f => !SystemAuditModel.Labels.ContainsKey(f.Key) || f.Value.Length > 500))
            ModelState.AddModelError("", "Bộ lọc không hợp lệ.");
        if (!ModelState.IsValid) return View(new SystemAuditModel { Query = query });
        try { return View(await audit.Search(query, ct)); }
        catch (ArgumentException ex) { ModelState.AddModelError("", ex.Message); return View(new SystemAuditModel { Query = query }); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to search system audit log");
            return View(new SystemAuditModel { Query = query, Error = "Không thể tải nhật ký. Vui lòng thử lại hoặc kiểm tra cấu hình cơ sở dữ liệu." });
        }
    }
}