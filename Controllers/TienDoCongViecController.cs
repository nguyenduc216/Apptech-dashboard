using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace ApptechDashboard.Controllers;

[Authorize]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class TienDoCongViecController(WorkProgressService progress, ILogger<TienDoCongViecController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(WorkBoardQuery query, CancellationToken ct)
    {
        if (!await progress.Allowed(User,"TienDoCongViec_View",ct)) return Forbid();
        if (query.From > query.To) ModelState.AddModelError("", "Ngày kết thúc không được trước ngày bắt đầu.");
        if (!ModelState.IsValid) return BadRequest(ModelState);
        return View(await progress.Board(query,User,ct));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(WorkMove move, CancellationToken ct)
    {
        if (!await progress.Allowed(User,"TienDoCongViec_Update",ct)) return Forbid();
        if (!ModelState.IsValid) return BadRequest(new { message="Dữ liệu cập nhật không hợp lệ." });
        try { await progress.Move(move,User,ct); return Json(new { message="Đã cập nhật trạng thái." }); }
        catch (FormatException) { return BadRequest(new { message="Phiên bản dữ liệu không hợp lệ." }); }
        catch (SqlException ex) when (ex.Number >= 51000) { return Conflict(new { message=ex.Message }); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { logger.LogError(ex,"Work progress move failed for {Id}",move.Id); return StatusCode(500,new { message="Không thể lưu trạng thái. Hãy làm mới và thử lại." }); }
    }
}

[Authorize]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class TrangThaiCongViecController(WorkProgressService progress, ILogger<TrangThaiCongViecController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (!await progress.Allowed(User,"TrangThaiCongViec_View",ct)) return Forbid();
        ViewBag.CanCreate=await progress.Allowed(User,"TrangThaiCongViec_Create",ct);
        ViewBag.CanUpdate=await progress.Allowed(User,"TrangThaiCongViec_Update",ct);
        ViewBag.CanToggle=await progress.Allowed(User,"TrangThaiCongViec_Toggle",ct);
        return View(await progress.Statuses(ct));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(WorkStatus form, CancellationToken ct)
    {
        if (!await progress.Allowed(User,form.Id==0?"TrangThaiCongViec_Create":"TrangThaiCongViec_Update",ct)) return Forbid();
        var old=(await progress.Statuses(ct)).FirstOrDefault(s=>s.Id==form.Id);
        if (form.Id!=0 && old is null) return NotFound();
        if(old is not null && old.Active!=form.Active && !await progress.Allowed(User,"TrangThaiCongViec_Toggle",ct)) return Forbid();
        if (!ModelState.IsValid) { TempData["WorkStatusError"]="Kiểm tra tên, mã (A-Z, 0-9, _), loại và màu HEX."; return RedirectToAction(nameof(Index)); }
        try { await progress.SaveStatus(form,ct); TempData["WorkStatusSuccess"]="Đã lưu trạng thái."; }
        catch(SqlException ex) when(ex.Number is 2601 or 2627) { TempData["WorkStatusError"]="Mã trạng thái đã tồn tại."; }
        catch(SqlException ex) when(ex.Number>=51000) { TempData["WorkStatusError"]=ex.Message; }
        catch(FormatException) { TempData["WorkStatusError"]="Dữ liệu đã thay đổi. Hãy thử lại."; }
        catch(Exception ex) when(ex is not OperationCanceledException) { logger.LogError(ex,"Saving work status failed"); TempData["WorkStatusError"]="Không thể lưu trạng thái."; }
        return RedirectToAction(nameof(Index));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id, string version, CancellationToken ct)
    {
        if(!await progress.Allowed(User,"TrangThaiCongViec_Toggle",ct)) return Forbid();
        var s=(await progress.Statuses(ct)).FirstOrDefault(s=>s.Id==id);
        if(s is null) return NotFound();
        s.Active=!s.Active; s.Version=version;
        try { await progress.SaveStatus(s,ct); }
        catch(Exception ex) when(ex is SqlException or FormatException) { TempData["WorkStatusError"]="Không thể đổi kích hoạt. Hãy tải lại dữ liệu."; }
        return RedirectToAction(nameof(Index));
    }
}
