using System.Net;
using System.Text;
using System.Text.Json;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

public sealed class ZaloRequestController(
    IZaloRequestService requestService,
    IZaloMessageService messageService,
    ILogger<ZaloRequestController> logger) : Controller
{
    [Authorize]
    [HttpPost("api/zalo/request-links")]
    public async Task<IActionResult> CreateLink(
        [FromBody] ZaloRequestLinkCreateRequest request,
        CancellationToken cancellationToken)
    {
        if (request.RequestId <= 0)
        {
            return BadRequest(new { success = false, message = "RequestId không hợp lệ." });
        }

        var result = await requestService.CreateLinkAsync(request.RequestId, cancellationToken);
        return result is null
            ? NotFound(new { success = false, message = "Không tìm thấy phiếu yêu cầu." })
            : Ok(new
            {
                success = true,
                token = result.Token,
                qrUrl = result.QrUrl,
                qrImageBase64 = result.QrImageBase64,
                status = result.Status,
                expiresAtUtc = result.ExpiresAtUtc,
                customerName = result.CustomerName,
                phoneNumber = result.PhoneNumber,
                zaloConnected = result.ZaloConnected,
                zaloDisplayName = result.ZaloDisplayName,
                zaloPhoneNumber = result.ZaloPhoneNumber,
                rated = result.Rated,
                ratingScore = result.RatingScore,
                ratingSubmittedAtUtc = result.RatingSubmittedAtUtc
            });
    }

    [AllowAnonymous]
    [HttpGet("api/zalo/request-links/{token}/status")]
    public async Task<IActionResult> Status(string token, CancellationToken cancellationToken)
    {
        var status = await requestService.GetStatusAsync(token, cancellationToken);
        return status is null
            ? NotFound(new { success = false, message = "Link không tồn tại." })
            : Ok(new
            {
                success = true,
                status = status.Status,
                openCount = status.OpenCount,
                zaloConnected = status.ZaloConnected,
                zaloDisplayName = status.ZaloDisplayName,
                zaloPhoneNumber = status.ZaloPhoneNumber,
                rated = status.Rated,
                ratingScore = status.RatingScore,
                ratingSubmittedAtUtc = status.RatingSubmittedAtUtc,
                lastOpenedAtUtc = status.LastOpenedAtUtc,
                expiresAtUtc = status.ExpiresAtUtc
            });
    }

    [Authorize]
    [HttpGet("api/customers/{customerId:int}/zalo-profile")]
    public async Task<IActionResult> CustomerZaloProfile(int customerId, CancellationToken cancellationToken)
    {
        var profile = await requestService.GetCustomerZaloProfileAsync(customerId, cancellationToken);
        return Ok(profile.Connected
            ? new
            {
                connected = true,
                zaloUserId = profile.ZaloUserId,
                zaloDisplayName = profile.ZaloDisplayName,
                zaloAvatarUrl = profile.ZaloAvatarUrl,
                zaloPhoneNumber = profile.ZaloPhoneNumber,
                isFollowingOa = profile.IsFollowingOa,
                connectedAtUtc = profile.ConnectedAtUtc,
                lastInteractionAtUtc = profile.LastInteractionAtUtc
            }
            : new { connected = false });
    }

    [Authorize]
    [HttpGet("api/requests/{requestId:int}/rating")]
    public async Task<IActionResult> RequestRating(int requestId, CancellationToken cancellationToken)
    {
        var rating = await requestService.GetRequestRatingAsync(requestId, cancellationToken);
        return Ok(rating.HasRating
            ? new
            {
                hasRating = true,
                ratingScore = rating.RatingScore,
                note = rating.Note,
                customerComment = rating.CustomerComment,
                submittedAtUtc = rating.SubmittedAtUtc,
                source = rating.Source,
                items = rating.Items.Select(item => new
                {
                    item.RequestWorkItemId,
                    item.WorkName,
                    item.RatingScore,
                    item.Note
                })
            }
            : new { hasRating = false });
    }

    [AllowAnonymous]
    [HttpGet("zalo/request/{token}")]
    public async Task<IActionResult> Landing(string token, CancellationToken cancellationToken)
    {
        var model = await requestService.OpenAsync(token, cancellationToken);
        if (model?.ZaloConnected == true && !model.IsRated)
        {
            return Redirect($"/zalo/request/{Uri.EscapeDataString(token)}/rating");
        }

        return Content(model is null ? RenderInvalid() : RenderLanding(model), "text/html; charset=utf-8");
    }

    [AllowAnonymous]
    [HttpGet("zalo/request/{token}/rating")]
    public async Task<IActionResult> Rating(string token, CancellationToken cancellationToken)
    {
        var model = await requestService.GetRatingViewAsync(token, cancellationToken);
        return Content(model is null ? RenderInvalid() : RenderRating(model), "text/html; charset=utf-8");
    }

    [AllowAnonymous]
    [HttpPost("api/zalo/request-ratings")]
    public async Task<IActionResult> SubmitRating(
        [FromBody] ZaloRequestRatingSubmit request,
        CancellationToken cancellationToken)
    {
        var (result, error) = await requestService.SubmitRatingAsync(request, cancellationToken);
        if (result is null)
        {
            return BadRequest(new { success = false, message = error });
        }

        try
        {
            await messageService.SendRatingResultMessageAsync(
                result.RequestId,
                result.RatingId,
                result.RatingScore,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Rating was saved but Zalo acknowledgement failed for request {RequestId}.", result.RequestId);
        }

        return Ok(new
        {
            success = true,
            message = "Cảm ơn anh/chị đã gửi đánh giá.",
            ratingId = result.RatingId
        });
    }

    private static string RenderLanding(ZaloRequestLandingView model)
    {
        var works = model.Works.Count == 0
            ? "<li>Chưa có danh sách công việc.</li>"
            : string.Join("", model.Works.Select(work =>
                $"<li><strong>{Encode(work.WorkName)}</strong><span>{Encode(work.Status ?? "Chưa cập nhật")}</span></li>"));
        var followUrl = string.IsNullOrWhiteSpace(model.OaId)
            ? "#"
            : $"https://zalo.me/{Uri.EscapeDataString(model.OaId)}";
        var execution = model.ExecutionDate?.ToString("dd/MM/yyyy HH:mm") ?? "Chưa xác định";

        return $$$"""
            <!doctype html>
            <html lang="vi">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>Phiếu {{{Encode(model.RequestCode)}}}</title>
                <style>
                    *{box-sizing:border-box}body{margin:0;background:#edf8f5;color:#073f38;font-family:Arial,sans-serif}
                    main{width:min(680px,calc(100% - 28px));margin:24px auto;background:#fff;border:1px solid #cce6df;border-radius:8px;overflow:hidden;box-shadow:0 16px 36px rgba(5,73,64,.12)}
                    header{padding:24px;background:#087d69;color:#fff}h1{margin:0 0 6px;font-size:25px}header p{margin:0;opacity:.9}
                    section{padding:22px;border-bottom:1px solid #e0eeea}h2{margin:0 0 14px;font-size:18px}.info{display:grid;grid-template-columns:1fr 1fr;gap:12px}
                    .info div{padding:12px;background:#f3faf8;border-radius:6px}.info span{display:block;color:#66807c;font-size:12px;margin-bottom:4px}
                    ul{list-style:none;padding:0;margin:0;display:grid;gap:8px}li{display:flex;justify-content:space-between;gap:12px;padding:12px;border:1px solid #d7e9e5;border-radius:6px}li span{color:#607975}
                    .actions{display:grid;gap:10px;padding:22px}.button{display:flex;align-items:center;justify-content:center;min-height:48px;padding:0 16px;border-radius:7px;text-decoration:none;font-weight:700}
                    .follow{background:#087d69;color:#fff}.rating{border:1px solid #087d69;color:#087d69;background:#fff}.hint{margin:0;color:#58736e;line-height:1.5;font-size:14px}
                    code{display:block;margin-top:10px;padding:10px;background:#eff7f5;border-radius:6px;overflow-wrap:anywhere;color:#28645b}
                    .verify-code{font-size:18px;font-weight:700}
                    .copy-code{min-height:42px;margin-top:10px;border:1px solid #9fdccc;border-radius:7px;background:#fff;color:#08735f;font:inherit;font-weight:700;cursor:pointer}
                    .copy-status{min-height:20px;margin-top:8px;color:#08735f;font-weight:700;font-size:13px}
                    @media(max-width:520px){.info{grid-template-columns:1fr}main{margin:12px auto}header,section,.actions{padding:18px}}
                </style>
            </head>
            <body>
                <main>
                    <header><h1>Phiếu {{{Encode(model.RequestCode)}}}</h1><p>Thông tin lịch làm việc và đánh giá chất lượng</p></header>
                    <section class="info">
                        <div><span>Khách hàng</span><strong>{{{Encode(model.CustomerName)}}}</strong></div>
                        <div><span>Số điện thoại</span><strong>{{{Encode(model.PhoneNumber ?? "-")}}}</strong></div>
                        <div><span>Ngày thực hiện</span><strong>{{{execution}}}</strong></div>
                        <div><span>Trạng thái đánh giá</span><strong>{{{(model.IsRated ? "Đã đánh giá" : "Chưa đánh giá")}}}</strong></div>
                    </section>
                    <section><h2>Công việc thực hiện</h2><ul>{{{works}}}</ul></section>
                    <section>
                        <h2>Kết nối Zalo OA</h2>
                        <p class="hint">Khách hàng cần chủ động quan tâm hoặc tương tác với OA. Sau khi Zalo gửi webhook hợp lệ, hệ thống mới cập nhật được Zalo ID.</p>
                        <code>Mã liên kết: {{{Encode(model.UserExternalId)}}}</code>
                    </section>
                    <section>
                        <h2>Ma xac nhan Zalo</h2>
                        <p class="hint">Neu anh/chi da quan tam OA, hay sao chep ma nay va gui vao khung chat Zalo OA de he thong ghi nhan thong tin Zalo.</p>
                        <code class="verify-code" data-verify-code="{{{Encode(model.Token)}}}">{{{Encode(model.Token)}}}</code>
                        <button class="copy-code" type="button" data-copy-code>Sao chep ma xac nhan</button>
                        <div class="copy-status" data-copy-status aria-live="polite"></div>
                    </section>
                    <div class="actions">
                        <a class="button follow" href="{{{followUrl}}}" target="_blank" rel="noopener">Quan tâm Zalo OA để nhận thông báo</a>
                        <a class="button rating" href="/zalo/request/{{{Uri.EscapeDataString(model.Token)}}}/rating">Tiếp tục đánh giá</a>
                    </div>
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

    private static string RenderRating(ZaloRequestLandingView model)
    {
        var items = string.Join("", model.Works.Select((work, index) => $$"""
            <article class="work" data-work-item>
                <input type="hidden" data-work-id value="{{work.RequestWorkItemId}}">
                <input type="hidden" data-work-name value="{{Encode(work.WorkName)}}">
                <strong>{{Encode(work.WorkName)}}</strong>
                <div class="stars" data-stars data-value="5" aria-label="Đánh giá công việc">
                    <button type="button" data-score="1">★</button><button type="button" data-score="2">★</button><button type="button" data-score="3">★</button><button type="button" data-score="4">★</button><button type="button" data-score="5">★</button>
                </div>
                <textarea data-work-note rows="2" placeholder="Ghi chú riêng cho công việc"></textarea>
            </article>
            """));

        return $$$"""
            <!doctype html>
            <html lang="vi">
            <head>
                <meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
                <title>Đánh giá {{{Encode(model.RequestCode)}}}</title>
                <style>
                    *{box-sizing:border-box}body{margin:0;background:#edf8f5;color:#073f38;font-family:Arial,sans-serif}
                    main{width:min(720px,calc(100% - 28px));margin:24px auto;background:#fff;border:1px solid #cce6df;border-radius:8px;padding:24px}
                    h1{margin:0 0 6px;font-size:25px}.sub{margin:0 0 22px;color:#607975}.field{display:grid;gap:7px;margin:16px 0}.field>span{font-weight:700}
                    textarea{width:100%;border:1px solid #c9ddd8;border-radius:6px;padding:11px;font:inherit;resize:vertical}
                    .stars{display:flex;gap:5px}.stars button{border:0;background:transparent;color:#d5ddd9;font-size:31px;padding:0;cursor:pointer}.stars button.active{color:#f1b51c}
                    .work{padding:14px;border:1px solid #d7e9e5;border-radius:7px;margin:10px 0}.work strong{display:block;margin-bottom:8px}
                    .submit{width:100%;min-height:50px;border:0;border-radius:7px;background:#087d69;color:#fff;font:inherit;font-weight:800;cursor:pointer}
                    .status{min-height:24px;margin-top:12px;font-weight:700;color:#087d69}.status.error{color:#b53737}
                    @media(max-width:520px){main{margin:12px auto;padding:18px}}
                </style>
            </head>
            <body>
                <main>
                    <h1>Đánh giá chất lượng công việc</h1>
                    <p class="sub">Phiếu {{{Encode(model.RequestCode)}}} · {{{Encode(model.CustomerName)}}}</p>
                    <form data-rating-form>
                        <div class="field"><span>Đánh giá tổng thể</span><div class="stars" data-stars data-value="5"><button type="button" data-score="1">★</button><button type="button" data-score="2">★</button><button type="button" data-score="3">★</button><button type="button" data-score="4">★</button><button type="button" data-score="5">★</button></div></div>
                        <div class="field"><span>Ghi chú</span><textarea data-note rows="3" placeholder="Ví dụ: Hoàn thành tốt"></textarea></div>
                        <div class="field"><span>Nhận xét của khách hàng</span><textarea data-comment rows="4" placeholder="Chia sẻ trải nghiệm của anh/chị"></textarea></div>
                        {{{items}}}
                        <button class="submit" type="submit">Gửi đánh giá</button>
                        <div class="status" data-status aria-live="polite"></div>
                    </form>
                </main>
                <script>
                    document.querySelectorAll('[data-stars]').forEach(group=>{
                        const paint=()=>{const value=Number(group.dataset.value||5);group.querySelectorAll('button').forEach(button=>button.classList.toggle('active',Number(button.dataset.score)<=value));};
                        group.querySelectorAll('button').forEach(button=>button.addEventListener('click',()=>{group.dataset.value=button.dataset.score;paint();}));
                        paint();
                    });
                    document.querySelector('[data-rating-form]').addEventListener('submit',async event=>{
                        event.preventDefault();
                        const form=event.currentTarget,status=form.querySelector('[data-status]'),submit=form.querySelector('.submit');
                        submit.disabled=true;status.className='status';status.textContent='Đang gửi đánh giá...';
                        const items=Array.from(form.querySelectorAll('[data-work-item]')).map(item=>({
                            requestWorkItemId:Number(item.querySelector('[data-work-id]').value)||null,
                            workName:item.querySelector('[data-work-name]').value,
                            ratingScore:Number(item.querySelector('[data-stars]').dataset.value||5),
                            note:item.querySelector('[data-work-note]').value
                        }));
                        const payload={token:'{{{JavaScriptEncoder(model.Token)}}}',ratingScore:Number(form.querySelector(':scope > .field [data-stars]').dataset.value||5),note:form.querySelector('[data-note]').value,customerComment:form.querySelector('[data-comment]').value,items};
                        try{
                            const response=await fetch('/api/zalo/request-ratings',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)});
                            const data=await response.json();
                            if(!response.ok)throw new Error(data.message||'Không thể gửi đánh giá.');
                            form.innerHTML='<h2>Cảm ơn anh/chị đã đánh giá</h2><p>Phản hồi đã được ghi nhận thành công.</p>';
                        }catch(error){status.className='status error';status.textContent=error.message;submit.disabled=false;}
                    });
                </script>
            </body>
            </html>
            """;
    }

    private static string RenderInvalid() => """
        <!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Link không hợp lệ</title></head>
        <body style="font-family:Arial,sans-serif;margin:0;background:#edf8f5;color:#073f38;display:grid;min-height:100vh;place-items:center"><main style="width:min(520px,calc(100% - 28px));background:#fff;padding:26px;border-radius:8px;border:1px solid #cce6df"><h1>Link không hợp lệ hoặc đã hết hạn</h1><p>Vui lòng liên hệ công ty để nhận lại mã QR mới.</p></main></body></html>
        """;

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
    private static string JavaScriptEncoder(string value) => JsonSerializer.Serialize(value).Trim('"');
}

public sealed class ZaloRequestLinkCreateRequest
{
    public int RequestId { get; set; }
}
