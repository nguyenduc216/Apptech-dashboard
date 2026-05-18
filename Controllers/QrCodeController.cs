using System.Security.Claims;
using System.Text.RegularExpressions;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ApptechDashboard.Controllers;

[Authorize]
public class QrCodeController(
    IQrCodeBatchService qrCodeBatchService,
    IVatTuService vatTuService,
    ILogger<QrCodeController> logger) : Controller
{
    private const string InvalidQrCodeMessage = "Mã QR không hợp lệ. Chỉ chấp nhận mã QR do hệ thống sinh theo dạng appTech-XXXXXXXXX.";
    private static readonly Regex AppTechQrCodePattern = new(
        "^appTech-[A-Za-z0-9]{9}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly IQrCodeBatchService _qrCodeBatchService = qrCodeBatchService;
    private readonly IVatTuService _vatTuService = vatTuService;
    private readonly ILogger<QrCodeController> _logger = logger;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        SetPageMetadata();
        return View(await BuildPageModelAsync(new QrCodeBatchRequestModel(), null, null, null, HttpContext.RequestAborted));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index([Bind(Prefix = "Request")] QrCodeBatchRequestModel request)
    {
        SetPageMetadata();

        if (!ModelState.IsValid)
        {
            return View(await BuildPageModelAsync(request, null, null, null, HttpContext.RequestAborted));
        }

        try
        {
            var result = await _qrCodeBatchService.GenerateBatchAsync(request, HttpContext.RequestAborted);

            return View(await BuildPageModelAsync(
                request,
                result.Items,
                result.GeneratedAtUtc,
                (result.FirstSequence, result.LastSequence),
                HttpContext.RequestAborted));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate QR batch from UI request.");
            ModelState.AddModelError(string.Empty, BuildDetailedQrErrorMessage(ex));
            return View(await BuildPageModelAsync(request, null, null, null, HttpContext.RequestAborted));
        }
    }

    [HttpGet]
    public IActionResult Preview([FromQuery] string? value)
    {
        try
        {
            var qrValue = value?.Trim();
            if (string.IsNullOrWhiteSpace(qrValue))
            {
                return BadRequest();
            }

            Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
            return Content(_qrCodeBatchService.GenerateSvgMarkup(qrValue), "image/svg+xml; charset=utf-8");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render QR preview for value {QrValue}.", value);
            return Problem(
                title: "Không thể tạo preview QR",
                detail: BuildDetailedQrErrorMessage(ex),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportPdf(QrCodePdfExportModel model)
    {
        if (model.Values.Count == 0)
        {
            return BadRequest();
        }

        try
        {
            var pdfBytes = _qrCodeBatchService.GeneratePdfDocument(model.Values, model.Request);
            var fileName = $"qr-list-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export QR PDF.");
            ModelState.AddModelError(string.Empty, BuildDetailedQrErrorMessage(ex));
            SetPageMetadata();
            return View("Index", await BuildPageModelAsync(model.Request, null, null, null, HttpContext.RequestAborted));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Print180([Bind(Prefix = "Request")] QrCodeBatchRequestModel request)
    {
        try
        {
            var fixedRequest = Build180LabelRequest(request.Print180Pages);
            var result = await _qrCodeBatchService.GenerateBatchAsync(fixedRequest, HttpContext.RequestAborted);
            var pdfBytes = _qrCodeBatchService.Generate180LabelSheetPdfDocument(
                result.Items.Select(item => item.Value).ToArray());

            Response.Headers["Content-Disposition"] = $"inline; filename=\"qr-180-{fixedRequest.Quantity}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.pdf\"";
            return File(pdfBytes, "application/pdf");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate QR 180-label print sheet from UI request.");
            ModelState.AddModelError(string.Empty, BuildDetailedQrErrorMessage(ex));
            SetPageMetadata();
            return View("Index", await BuildPageModelAsync(request, null, null, null, HttpContext.RequestAborted));
        }
    }

    [HttpGet]
    public async Task<IActionResult> SearchAssignmentTargets([FromQuery] QrCodeAssignmentSearchModel model)
    {
        NormalizeAssignmentSearch(model);

        if (!TryValidateModel(model))
        {
            return Json(new
            {
                succeeded = false,
                errorMessage = "Điều kiện tìm kiếm không hợp lệ."
            });
        }

        var items = await _vatTuService.SearchForQrAssignmentAsync(model, HttpContext.RequestAborted);
        return Json(new
        {
            succeeded = true,
            totalCount = items.Count,
            items
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignToVatTu([FromForm] QrCodeAssignmentApplyModel model)
    {
        var qrValue = NormalizeQrCode(model.QRCode);

        if (model.ItemId <= 0)
        {
            return Json(new
            {
                succeeded = false,
                errorMessage = "Không xác định được vật tư cần gán QR."
            });
        }

        if (string.IsNullOrWhiteSpace(qrValue) || !AppTechQrCodePattern.IsMatch(qrValue))
        {
            return Json(new
            {
                succeeded = false,
                errorMessage = InvalidQrCodeMessage
            });
        }

        var result = await _vatTuService.AssignQrCodeAsync(
            model.ItemId,
            qrValue,
            GetCurrentAuditUser(),
            HttpContext.RequestAborted);

        return Json(new
        {
            succeeded = result.Succeeded,
            errorMessage = result.ErrorMessage,
            itemId = model.ItemId,
            qrCode = qrValue
        });
    }

    private static string BuildDetailedQrErrorMessage(Exception exception)
    {
        var baseMessage = exception.GetBaseException().Message;
        return $"Không thể tạo QR lúc này. Chi tiết: {baseMessage}";
    }

    private void SetPageMetadata()
    {
        ViewData["Title"] = "Tạo mã QR";
        ViewData["Breadcrumb"] = "Trang chủ / QR Code / Tạo mã QR";
    }

    private static QrCodeBatchRequestModel Build180LabelRequest(int pageCount)
    {
        var normalizedPageCount = Math.Clamp(pageCount, 1, 50);
        return new QrCodeBatchRequestModel
        {
            Quantity = 180 * normalizedPageCount,
            QrPerRow = 10,
            QrWidth = 17,
            QrHeight = 17,
            Print180Pages = normalizedPageCount
        };
    }

    private async Task<QrCodeBatchPageViewModel> BuildPageModelAsync(
        QrCodeBatchRequestModel request,
        IReadOnlyList<QrCodePrintItem>? items,
        DateTimeOffset? generatedAtUtc,
        (long FirstSequence, long LastSequence)? sequenceRange,
        CancellationToken cancellationToken)
    {
        var (khoOptions, hangHoaOptions, _) = await _vatTuService.GetLookupDataAsync(cancellationToken);

        return new QrCodeBatchPageViewModel
        {
            Request = request,
            Items = items ?? [],
            GeneratedAtUtc = generatedAtUtc,
            FirstSequence = sequenceRange?.FirstSequence,
            LastSequence = sequenceRange?.LastSequence,
            Assignment = new QrCodeAssignmentViewModel
            {
                KhoOptions = khoOptions,
                HangHoaOptions = hangHoaOptions
            }
        };
    }

    private static void NormalizeAssignmentSearch(QrCodeAssignmentSearchModel model)
    {
        model.TenChiTiet = NormalizeKeyword(model.TenChiTiet);
        model.ViTriLuuKho = NormalizeKeyword(model.ViTriLuuKho);
        model.MaSoLo = NormalizeKeyword(model.MaSoLo);
        model.MaPhieuNhap = NormalizeKeyword(model.MaPhieuNhap);
        model.QrStatus = QrCodeAssignmentQrStatus.MissingQr;
        if (model.HangHoaId is <= 0)
        {
            model.HangHoaId = null;
        }

        if (model.KhoId is <= 0)
        {
            model.KhoId = null;
        }
    }

    private string GetCurrentAuditUser()
    {
        var username = User.Identity?.Name
            ?? User.FindFirstValue(ClaimTypes.Name)
            ?? User.FindFirstValue("display_name")
            ?? "system";

        return username.Trim();
    }

    private static string? NormalizeQrCode(string? qrCode)
    {
        var normalizedValue = qrCode?.Trim();
        return string.IsNullOrWhiteSpace(normalizedValue) ? null : normalizedValue;
    }

    private static string? NormalizeKeyword(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeQrStatus(string? qrStatus)
    {
        var normalized = qrStatus?.Trim().ToLowerInvariant();
        return normalized switch
        {
            QrCodeAssignmentQrStatus.HasQr => QrCodeAssignmentQrStatus.HasQr,
            QrCodeAssignmentQrStatus.MissingQr => QrCodeAssignmentQrStatus.MissingQr,
            _ => QrCodeAssignmentQrStatus.All
        };
    }
}
