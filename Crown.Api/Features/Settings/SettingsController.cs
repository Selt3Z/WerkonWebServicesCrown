using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Crown.Contracts.Auth;
using Crown.Contracts.Common;
using Crown.Contracts.Import;
using Crown.Contracts.Settings;
using Crown.Infrastructure.Api;
using Crown.Infrastructure.Backups;
using Crown.Infrastructure.Export;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Import;
using Crown.Infrastructure.Settings;

namespace Crown.Features.Settings;

[ApiController]
[Route("api/settings")]
[Authorize(Policy = AuthorizationPolicies.BusinessData)]
public sealed class SettingsController : ControllerBase
{
    private readonly AppSettingsService _appSettingsService;
    private readonly BackupStatusReader _backupStatusReader;
    private readonly DataExportService _dataExportService;
    private readonly CatalogPartImportService _catalogPartImportService;
    private readonly WarehousePartImportService _warehousePartImportService;

    public SettingsController(
        AppSettingsService appSettingsService,
        BackupStatusReader backupStatusReader,
        DataExportService dataExportService,
        CatalogPartImportService catalogPartImportService,
        WarehousePartImportService warehousePartImportService)
    {
        _appSettingsService = appSettingsService;
        _backupStatusReader = backupStatusReader;
        _dataExportService = dataExportService;
        _catalogPartImportService = catalogPartImportService;
        _warehousePartImportService = warehousePartImportService;
    }

    [HttpGet]
    public async Task<ActionResult<SettingsResponse>> Get(CancellationToken cancellationToken)
    {
        return await _appSettingsService.GetSettingsAsync(cancellationToken);
    }

    [HttpPut]
    [Authorize(Policy = AuthorizationPolicies.ManageUsers)]
    public async Task<ActionResult<SettingsResponse>> Update(
        [FromBody] UpdateSettingsRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _appSettingsService.UpdateSettingsAsync(request, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiErrors.FromArgumentException(ex));
        }
    }

    [HttpPut("timezone")]
    [Authorize(Policy = AuthorizationPolicies.ManageUsers)]
    public async Task<ActionResult<SettingsResponse>> UpdateTimeZone(
        [FromBody] UpdateTimeZoneRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _appSettingsService.UpdateSettingsAsync(
                new UpdateSettingsRequest { TimeZoneId = request.TimeZoneId },
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiErrors.FromArgumentException(ex));
        }
    }

    [HttpGet("organization")]
    [Authorize(Policy = AuthorizationPolicies.ManageUsers)]
    public async Task<ActionResult<OrganizationProfileResponse>> GetOrganization(
        CancellationToken cancellationToken)
    {
        return await _appSettingsService.GetOrganizationProfileAsync(cancellationToken);
    }

    [HttpPut("organization")]
    [Authorize(Policy = AuthorizationPolicies.ManageUsers)]
    public async Task<ActionResult<OrganizationProfileResponse>> UpdateOrganization(
        [FromBody] UpdateOrganizationProfileRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _appSettingsService.UpdateOrganizationProfileAsync(request, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiErrors.FromArgumentException(ex));
        }
    }

    [HttpPut("organization/logo")]
    [Authorize(Policy = AuthorizationPolicies.ManageUsers)]
    public async Task<ActionResult<OrganizationProfileResponse>> UploadOrganizationLogo(
        [FromBody] UploadOrganizationLogoRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _appSettingsService.UploadOrganizationLogoAsync(request, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiErrors.FromArgumentException(ex));
        }
    }

    [HttpGet("backup-status")]
    [Authorize(Policy = AuthorizationPolicies.ManageUsers)]
    public async Task<ActionResult<BackupStatusResponse>> GetBackupStatus(CancellationToken cancellationToken)
    {
        return await _backupStatusReader.ReadAsync(cancellationToken);
    }

    [HttpDelete("organization/logo")]
    [Authorize(Policy = AuthorizationPolicies.ManageUsers)]
    public async Task<ActionResult<OrganizationProfileResponse>> DeleteOrganizationLogo(
        CancellationToken cancellationToken)
    {
        return await _appSettingsService.DeleteOrganizationLogoAsync(cancellationToken);
    }

    [HttpGet("export")]
    [Authorize(Policy = AuthorizationPolicies.ManageUsers)]
    public async Task<IActionResult> ExportData(
        [FromQuery] string? format = "json",
        CancellationToken cancellationToken = default)
    {
        var normalized = (format ?? "json").Trim().ToLowerInvariant();
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

        if (normalized is "csv" or "zip")
        {
            var zipBytes = await _dataExportService.BuildCsvZipAsync(cancellationToken);
            return File(zipBytes, "application/zip", $"crown-export-{stamp}.zip");
        }

        var jsonBytes = await _dataExportService.BuildJsonAsync(cancellationToken);
        return File(jsonBytes, "application/json; charset=utf-8", $"crown-export-{stamp}.json");
    }

    [HttpPost("import-catalog-parts/preview")]
    [Authorize(Policy = AuthorizationPolicies.ManageUsers)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(SpreadsheetImportFileHelper.MaxFileBytes + 1024 * 1024)]
    public async Task<ActionResult<SpreadsheetImportPreviewResponse>> PreviewCatalogPartsImport(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        return await PreviewSpreadsheetAsync(file, _catalogPartImportService.Preview, cancellationToken);
    }

    [HttpPost("import-catalog-parts/commit")]
    [Authorize(Policy = AuthorizationPolicies.ManageUsers)]
    // base64 от 5 МБ ≈ 6.7 МБ; берём запас под JSON-обёртку.
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<ActionResult<SpreadsheetImportCommitResponse>> CommitCatalogPartsImport(
        [FromBody] SpreadsheetImportCommitRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _catalogPartImportService.CommitAsync(request, cancellationToken);
        }
        catch (SpreadsheetImportException ex)
        {
            return BadRequest(ApiErrors.FromCode(ex.Code));
        }
    }

    [HttpPost("import-warehouse/preview")]
    [Authorize(Policy = AuthorizationPolicies.ManageUsers)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(SpreadsheetImportFileHelper.MaxFileBytes + 1024 * 1024)]
    public async Task<ActionResult<SpreadsheetImportPreviewResponse>> PreviewWarehouseImport(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (!await _appSettingsService.GetWarehouseEnabledAsync(cancellationToken))
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.WarehouseDisabled));
        }

        return await PreviewSpreadsheetAsync(file, _warehousePartImportService.Preview, cancellationToken);
    }

    [HttpPost("import-warehouse/commit")]
    [Authorize(Policy = AuthorizationPolicies.ManageUsers)]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<ActionResult<SpreadsheetImportCommitResponse>> CommitWarehouseImport(
        [FromBody] SpreadsheetImportCommitRequest request,
        CancellationToken cancellationToken)
    {
        if (!await _appSettingsService.GetWarehouseEnabledAsync(cancellationToken))
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.WarehouseDisabled));
        }

        try
        {
            return await _warehousePartImportService.CommitAsync(request, cancellationToken);
        }
        catch (SpreadsheetImportException ex)
        {
            return BadRequest(ApiErrors.FromCode(ex.Code));
        }
    }

    private static async Task<ActionResult<SpreadsheetImportPreviewResponse>> PreviewSpreadsheetAsync(
        IFormFile? file,
        Func<byte[], string?, SpreadsheetImportPreviewResponse> preview,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return new BadRequestObjectResult(ApiErrors.FromCode(ApiErrorCodes.SpreadsheetImportInvalidFile));
        }

        if (file.Length > SpreadsheetImportFileHelper.MaxFileBytes)
        {
            return new BadRequestObjectResult(ApiErrors.FromCode(ApiErrorCodes.SpreadsheetImportFileTooLarge));
        }

        try
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream((int)file.Length);
            await stream.CopyToAsync(buffer, cancellationToken);

            return preview(buffer.ToArray(), file.FileName);
        }
        catch (SpreadsheetImportException ex)
        {
            return new BadRequestObjectResult(ApiErrors.FromCode(ex.Code));
        }
    }
}
