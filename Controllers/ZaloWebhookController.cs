using ApptechDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApptechDashboard.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/zalo/webhook")]
public sealed class ZaloWebhookController(IZaloWebhookService webhookService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        var signature = Request.Headers["X-ZEvent-Signature"].FirstOrDefault();
        var result = await webhookService.ProcessAsync(rawBody, signature, cancellationToken);

        return result.Accepted
            ? Ok(new { message = result.Message, processed = result.Processed })
            : Unauthorized(new { message = result.Message });
    }
}
