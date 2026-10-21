using Microsoft.AspNetCore.Authentication.Cookies;

namespace ApptechDashboard.Configuration;

// FEATURE_ID: APPTECH-WORK-PROGRESS-KANBAN; CHANGE_ID: APPTECH-20261020-WORK-PROGRESS-005
public static class CookieAuthorization
{
    public static void ConfigureAccessDenied(CookieAuthenticationOptions options)
    {
        // An authenticated denial must never go to Login, which redirects back to ReturnUrl.
        options.AccessDeniedPath = "/khong-co-quyen";
        options.Events.OnRedirectToAccessDenied = async context =>
        {
            if (context.Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                context.Request.Headers.Accept.Any(value => value?.Contains("application/json",StringComparison.OrdinalIgnoreCase) == true))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { message="Bạn không có quyền thực hiện thao tác này." });
                return;
            }
            context.Response.Redirect(context.RedirectUri);
        };
    }
}
