using Microsoft.Extensions.Configuration;
using WerkonWebServicesRatchet.Contracts.Settings;
using WerkonWebServicesRatchet.Infrastructure.Settings;

namespace WerkonWebServicesRatchet.Tests;

public sealed class LogoSignatureTests
{
    private static readonly byte[] PngHeader =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    private static readonly byte[] JpegHeader =
        [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];

    [Fact]
    public async Task Upload_ValidPngSignature_Succeeds()
    {
        var service = CreateService(out var dbContext);
        await using var _ = dbContext;

        var response = await service.UploadOrganizationLogoAsync(new UploadOrganizationLogoRequest
        {
            ContentType = "image/png",
            DataBase64 = Convert.ToBase64String(PngHeader)
        });

        Assert.True(response.HasLogo);
    }

    [Fact]
    public async Task Upload_TextDataDeclaredAsPng_Throws()
    {
        var service = CreateService(out var dbContext);
        await using var _ = dbContext;

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UploadOrganizationLogoAsync(new UploadOrganizationLogoRequest
            {
                ContentType = "image/png",
                DataBase64 = Convert.ToBase64String("<svg>not an image</svg>"u8.ToArray())
            }));

        Assert.Contains("not a valid", exception.Message);
    }

    [Fact]
    public async Task Upload_JpegDataDeclaredAsPng_Throws()
    {
        var service = CreateService(out var dbContext);
        await using var _ = dbContext;

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UploadOrganizationLogoAsync(new UploadOrganizationLogoRequest
            {
                ContentType = "image/png",
                DataBase64 = Convert.ToBase64String(JpegHeader)
            }));

        Assert.Contains("does not match", exception.Message);
    }

    private static AppSettingsService CreateService(out Infrastructure.Persistence.AppDbContext dbContext)
    {
        dbContext = TestHelpers.CreateDbContext();
        var configuration = new ConfigurationBuilder().Build();

        return new AppSettingsService(dbContext, TestHelpers.CreateAppTimeZone(), configuration);
    }
}
