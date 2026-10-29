using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

/// <summary>
/// Прокси активного логотипа с API (anonymous). Нужен, т.к. img идёт на web origin,
/// а не через server-side CrownApiClient.
/// </summary>
[AllowAnonymous]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class BrandLogoModel(IHttpClientFactory httpClientFactory) : PageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        var client = httpClientFactory.CreateClient("CrownApiAnonymous");
        using var response = await client.GetAsync("api/settings/brand-logo", HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            return NotFound();
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(HttpContext.RequestAborted);
        if (bytes.Length == 0)
        {
            return NotFound();
        }

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
        return File(bytes, contentType);
    }
}
