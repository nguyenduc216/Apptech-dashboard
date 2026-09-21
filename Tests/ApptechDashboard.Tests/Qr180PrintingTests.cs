using System.ComponentModel.DataAnnotations;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using ApptechDashboard.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ApptechDashboard.Tests;

public sealed class Qr180PrintingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"apptech-qr180-{Guid.NewGuid():N}");
    private readonly QrCodeBatchService _service;

    public Qr180PrintingTests()
    {
        Directory.CreateDirectory(_root);
        _service = new QrCodeBatchService(new TestEnvironment(_root), NullLogger<QrCodeBatchService>.Instance);
    }

    [Fact]
    public void DefaultSettings_ReproduceLegacyGeometry()
    {
        var settings = new Qr180PrintSettings();
        Assert.Equal(new Qr180LayoutPosition(8.75m, 14.25m, 14.5m), _service.Get180LayoutPosition(0, settings));
        Assert.Equal(new Qr180LayoutPosition(188.75m, 269.25m, 14.5m), _service.Get180LayoutPosition(179, settings));
    }

    [Fact]
    public void Offsets_MoveEveryPositionBySameAmount()
    {
        var baseline = new Qr180PrintSettings();
        var adjusted = new Qr180PrintSettings { OffsetX = -0.75m, OffsetY = 1.2m };
        foreach (var index in new[] { 0, 9, 90, 179 })
        {
            var before = _service.Get180LayoutPosition(index, baseline);
            var after = _service.Get180LayoutPosition(index, adjusted);
            Assert.Equal(-0.75m, after.LeftMm - before.LeftMm);
            Assert.Equal(1.2m, after.TopMm - before.TopMm);
        }
    }

    [Fact]
    public void PitchChanges_AccumulateByColumnAndRow()
    {
        var baseline = new Qr180PrintSettings();
        var adjusted = new Qr180PrintSettings { PitchX = 20.1m, PitchY = 15.1m };
        var first = _service.Get180LayoutPosition(0, adjusted);
        var last = _service.Get180LayoutPosition(179, adjusted);
        var baselineFirst = _service.Get180LayoutPosition(0, baseline);
        var baselineLast = _service.Get180LayoutPosition(179, baseline);
        Assert.Equal(0.05m, first.LeftMm - baselineFirst.LeftMm);
        Assert.Equal(0.05m, first.TopMm - baselineFirst.TopMm);
        Assert.Equal(0.95m, last.LeftMm - baselineLast.LeftMm);
        Assert.Equal(1.75m, last.TopMm - baselineLast.TopMm);
    }

    [Fact]
    public void TwoPagePdf_Maps360ValuesAndResetsSecondPageGeometry()
    {
        var values = Enumerable.Range(1, 360).Select(index => $"appTech-{index:000000000}").ToArray();
        var settings = new Qr180PrintSettings();
        var placements = _service.Get180PagePlacements(values, settings);
        var pdf = _service.Generate180LabelSheetPdfDocument(values, settings);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.Equal(2, Count(text, "/Type /Page "));
        Assert.Equal(360, placements.Count);
        Assert.Equal(180, placements.Count(item => item.PageIndex == 0));
        Assert.Equal(180, placements.Count(item => item.PageIndex == 1));
        var page2First = placements.Single(item => item.ValueIndex == 180);
        Assert.Equal(1, page2First.PageIndex);
        Assert.Equal(0, page2First.IndexWithinPage);
        Assert.Equal(values[180], page2First.Value);
        Assert.Equal(_service.Get180LayoutPosition(0, settings), page2First.Position);
    }

    [Fact]
    public void CalibrationPdf_DoesNotCreateSequenceFile()
    {
        var pdf = _service.Generate180CalibrationPdfDocument(new Qr180PrintSettings());
        Assert.NotEmpty(pdf);
        Assert.False(File.Exists(Path.Combine(_root, "App_Data", "qr-sequence.txt")));
    }

    [Fact]
    public void CalibrationPdf_DrawsOneQrSizeBoxPerLabelAndSizeChangesBox()
    {
        var small = System.Text.Encoding.ASCII.GetString(_service.Generate180CalibrationPdfDocument(new Qr180PrintSettings { QrSize = 10m }));
        var large = System.Text.Encoding.ASCII.GetString(_service.Generate180CalibrationPdfDocument(new Qr180PrintSettings { QrSize = 18m }));

        Assert.Equal(180, Count(small, " re S"));
        Assert.Equal(180, Count(large, " re S"));
        Assert.Contains("28.346 28.346 re S", small);
        Assert.Contains("51.024 51.024 re S", large);
        Assert.NotEqual(small, large);
    }

    [Fact]
    public void ProfileSaveDecision_InsertsNew_UpdatesCustom_AndNeverUpdatesDefault()
    {
        Assert.Equal(Qr180ProfileSaveMode.Insert, Qr180PrinterProfileService.DecideSave(null, false, false).Mode);
        Assert.Equal(new Qr180ProfileSaveDecision(Qr180ProfileSaveMode.Update, 7), Qr180PrinterProfileService.DecideSave(7, true, false));
        Assert.Equal(new Qr180ProfileSaveDecision(Qr180ProfileSaveMode.Insert, null), Qr180PrinterProfileService.DecideSave(1, true, true));
    }

    [Fact]
    public void CustomProfile_CannotUseReservedDefaultName()
    {
        var model = new Qr180ProfileSaveRequest { ProfileName = "  MẶC ĐỊNH  " };
        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), results, true));
        Assert.Contains(results, result => result.ErrorMessage?.Contains("dành cho cấu hình hệ thống") == true);
    }

    [Fact]
    public void ActiveProfileResolution_LoadsRequestedProfileValues()
    {
        var profiles = new[]
        {
            new Qr180PrinterProfile { Id = 1, ProfileName = "Mặc định", IsDefault = true },
            new Qr180PrinterProfile { Id = 5, ProfileName = "Canon 2900", OffsetX = -0.75m, OffsetY = 1.2m, PitchX = 19.95m, PitchY = 15.1m, QrSize = 14.4m }
        };
        var resolved = QrCodeController.ResolvePrint180Request(null, profiles, 5);
        Assert.Equal(5, resolved.ProfileId);
        Assert.Equal(-0.75m, resolved.OffsetX);
        Assert.Equal(1.2m, resolved.OffsetY);
        Assert.Equal(19.95m, resolved.PitchX);
        Assert.Equal(15.1m, resolved.PitchY);
        Assert.Equal(14.4m, resolved.QrSize);
    }

    [Fact]
    public async Task SaveProfile_RedirectsWithSavedProfileId()
    {
        var profileService = new Mock<IQr180PrinterProfileService>();
        profileService.Setup(service => service.SaveAsync(It.IsAny<Qr180ProfileSaveRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, null, 12));
        var controller = new QrCodeController(
            Mock.Of<IQrCodeBatchService>(),
            profileService.Object,
            Mock.Of<IVatTuService>(),
            NullLogger<QrCodeController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>())
        };

        var result = await controller.Save180Profile(new Qr180ProfileSaveRequest { ProfileName = "Canon 2900" });
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal(12, redirect.RouteValues?["printerProfileId"]);
    }

    [Theory]
    [InlineData(-10.01, 0, 20, 15, 14.5)]
    [InlineData(0, 0, 17.99, 15, 14.5)]
    [InlineData(0, 0, 20, 12.99, 14.5)]
    [InlineData(0, 0, 20, 15, 18.01)]
    [InlineData(10, 10, 22, 17, 18)]
    public void InvalidSettings_AreRejected(double offsetX, double offsetY, double pitchX, double pitchY, double qrSize)
    {
        var model = new Qr180PrintSettings { OffsetX = (decimal)offsetX, OffsetY = (decimal)offsetY, PitchX = (decimal)pitchX, PitchY = (decimal)pitchY, QrSize = (decimal)qrSize };
        Assert.False(Validator.TryValidateObject(model, new ValidationContext(model), [], true));
    }

    private static int Count(string value, string token) => (value.Length - value.Replace(token, string.Empty).Length) / token.Length;

    public void Dispose() => Directory.Delete(_root, true);

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(root);
    }
}
