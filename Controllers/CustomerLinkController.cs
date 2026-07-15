using ApptechDashboard.Configuration;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

public sealed class CustomerLinkController(
    ICustomerLinkService customerLinkService,
    IZaloSettingsService settingsService) : Controller
{
    [Authorize]
    [HttpPost("api/customer-links")]
    public async Task<IActionResult> Create([FromBody] CustomerLinkCreateRequest request, CancellationToken cancellationToken)
    {
        if (request.CustomerId <= 0)
        {
            return BadRequest(new { message = "customerId is required." });
        }

        var result = await customerLinkService.CreateLinkAsync(
            request.CustomerId,
            request.BookingId,
            request.Purpose ?? "ConnectZalo",
            request.ExpiresInDays <= 0 ? 30 : request.ExpiresInDays,
            cancellationToken);

        return Ok(new
        {
            link = result.Link,
            token = result.Token,
            userExternalId = result.UserExternalId
        });
    }

    [AllowAnonymous]
    [HttpGet("zalo/connect/{token}")]
    public async Task<IActionResult> Connect(string token, CancellationToken cancellationToken)
    {
        var link = await customerLinkService.RegisterClickAsync(token, cancellationToken);
        if (link is null)
        {
            return Content(RenderInvalidLink(), "text/html; charset=utf-8");
        }

        return Content(RenderConnectPage(link, settingsService.Current), "text/html; charset=utf-8");
    }

    private static string RenderInvalidLink()
    {
        return """
            <!doctype html>
            <html lang="vi">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Link khong hop le</title></head>
            <body style="font-family:Arial,sans-serif;margin:0;background:#eef8f5;color:#063b35;display:grid;min-height:100vh;place-items:center">
                <main style="width:min(520px,calc(100% - 32px));background:white;border-radius:16px;padding:28px;box-shadow:0 18px 45px rgba(4,58,54,.16)">
                    <h1>Link khong hop le hoac da het han</h1>
                    <p>Vui long lien he cong ty de nhan lai link ket noi Zalo moi.</p>
                </main>
            </body>
            </html>
            """;
    }

    private static string RenderConnectPage(ZaloCustomerLinkView link, ZaloOptions options)
    {
        var oaId = System.Net.WebUtility.HtmlEncode(options.OaId ?? string.Empty);
        var verifyCode = System.Net.WebUtility.HtmlEncode(link.Token);
        var userExternalId = System.Net.WebUtility.HtmlEncode(link.UserExternalId);
        var customerName = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(link.CustomerName) ? "quy khach" : link.CustomerName);
        var followHref = string.IsNullOrWhiteSpace(options.OaId)
            ? "#"
            : $"https://zalo.me/{Uri.EscapeDataString(options.OaId)}";

        return $$"""
            <!doctype html>
            <html lang="vi">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>Ket noi Zalo OA</title>
                <style>
                    body{font-family:Arial,sans-serif;margin:0;background:#eaf8f4;color:#063b35;min-height:100vh;display:grid;place-items:center}
                    main{width:min(560px,calc(100% - 32px));background:#fff;border-radius:18px;padding:28px;box-shadow:0 18px 45px rgba(4,58,54,.18)}
                    h1{font-size:26px;margin:0 0 12px}
                    p{line-height:1.55;color:#2a615b}
                    .actions{display:grid;gap:10px;margin-top:18px}
                    a,button{display:inline-flex;align-items:center;justify-content:center;min-height:48px;padding:0 18px;border:0;border-radius:12px;background:#15b894;color:#fff;text-decoration:none;font:inherit;font-weight:700;cursor:pointer}
                    button{background:#fff;color:#08735f;border:1px solid #9fdccc}
                    code{display:block;padding:12px;border-radius:10px;background:#eff8f6;color:#0b6158;overflow:auto;font-size:18px;font-weight:700;letter-spacing:.03em}
                    small{display:block;margin-top:8px;color:#5a746f;line-height:1.45}
                    .status{min-height:22px;margin-top:10px;color:#08735f;font-weight:700}
                </style>
            </head>
            <body>
                <main>
                    <h1>Ket noi Zalo voi cong ty</h1>
                    <p>Xin chao <strong>{{customerName}}</strong>. Neu anh/chi da quan tam Zalo OA, chi can sao chep ma xac nhan ben duoi va nhan vao khung chat OA.</p>
                    <code data-verify-code="{{verifyCode}}" data-oa-id="{{oaId}}" data-user-external-id="{{userExternalId}}">{{verifyCode}}</code>
                    <small>He thong se doc ma nay tu webhook Zalo de ghi nhan dung khach hang va luu Zalo User ID.</small>
                    <div class="actions">
                        <button type="button" data-copy-code>Sao chep ma xac nhan</button>
                        <a href="{{followHref}}" target="_blank" rel="noopener">Mo Zalo OA de gui ma</a>
                    </div>
                    <div class="status" data-copy-status aria-live="polite"></div>
                </main>
                <script>
                    const code = document.querySelector("[data-verify-code]")?.dataset.verifyCode || "";
                    const status = document.querySelector("[data-copy-status]");
                    document.querySelector("[data-copy-code]")?.addEventListener("click", async () => {
                        try {
                            await navigator.clipboard.writeText(code);
                            status.textContent = "Da sao chep ma. Hay dan ma vao chat Zalo OA.";
                        } catch {
                            status.textContent = "Hay sao chep ma hien thi phia tren va gui vao Zalo OA.";
                        }
                    });
                </script>
            </body>
            </html>
            """;
    }
}

public sealed class CustomerLinkCreateRequest
{
    public int CustomerId { get; set; }
    public int? BookingId { get; set; }
    public string? Purpose { get; set; }
    public int ExpiresInDays { get; set; } = 30;
}
