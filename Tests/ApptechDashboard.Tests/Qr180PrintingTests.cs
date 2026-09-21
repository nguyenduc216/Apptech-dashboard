using System.ComponentModel.DataAnnotations;
using ApptechDashboard.Models;
using ApptechDashboard.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
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
    public void TwoPagePdf_Has360CodesAndResetsPageGeometry()
    {
        var values = Enumerable.Range(1, 360).Select(index => $"appTech-{index:000000000}").ToArray();
        var pdf = _service.Generate180LabelSheetPdfDocument(values, new Qr180PrintSettings());
        var text = System.Text.Encoding.ASCII.GetString(pdf);
        Assert.Equal(2, Count(text, "/Type /Page "));
        Assert.Equal(_service.Get180LayoutPosition(0, new()), _service.Get180LayoutPosition(180 % 180, new()));
    }

    [Fact]
    public void CalibrationPdf_DoesNotCreateSequenceFile()
    {
        var pdf = _service.Generate180CalibrationPdfDocument(new Qr180PrintSettings());
        Assert.NotEmpty(pdf);
        Assert.False(File.Exists(Path.Combine(_root, "App_Data", "qr-sequence.txt")));
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
