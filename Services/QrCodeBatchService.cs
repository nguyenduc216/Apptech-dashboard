using System.Globalization;
using System.Text;
using ApptechDashboard.Models;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace ApptechDashboard.Services;

public interface IQrCodeBatchService
{
    Task<QrCodeBatchGenerationResult> GenerateBatchAsync(QrCodeBatchRequestModel request, CancellationToken cancellationToken = default);
    string GenerateSvgMarkup(string value);
    byte[] GeneratePdfDocument(IReadOnlyList<string> values, QrCodeBatchRequestModel request);
    byte[] Generate180LabelSheetPdfDocument(IReadOnlyList<string> values);
}

public sealed class QrCodeBatchService(
    IWebHostEnvironment webHostEnvironment,
    ILogger<QrCodeBatchService> logger) : IQrCodeBatchService
{
    private const string Prefix = "appTech-";
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const int CodeLength = 9;
    private static readonly SemaphoreSlim SequenceLock = new(1, 1);
    private readonly IWebHostEnvironment _webHostEnvironment = webHostEnvironment;
    private readonly ILogger<QrCodeBatchService> _logger = logger;

    public async Task<QrCodeBatchGenerationResult> GenerateBatchAsync(
        QrCodeBatchRequestModel request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var (firstSequence, lastSequence) = await ReserveSequenceRangeAsync(request.Quantity, cancellationToken);
            var items = new List<QrCodePrintItem>(request.Quantity);

            for (var index = 0; index < request.Quantity; index++)
            {
                var sequence = firstSequence + index;
                var value = BuildQrValue(sequence);

                items.Add(new QrCodePrintItem
                {
                    Index = index + 1,
                    Value = value,
                    SvgMarkup = GenerateSvgMarkup(value)
                });
            }

            return new QrCodeBatchGenerationResult
            {
                Items = items,
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                FirstSequence = firstSequence,
                LastSequence = lastSequence
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to generate QR batch. Quantity={Quantity}, QrPerRow={QrPerRow}, QrWidth={QrWidth}, QrHeight={QrHeight}, SequenceFilePath={SequenceFilePath}",
                request.Quantity,
                request.QrPerRow,
                request.QrWidth,
                request.QrHeight,
                GetSequenceFilePath());
            throw;
        }
    }

    public string GenerateSvgMarkup(string value)
    {
        var qrValue = value?.Trim();
        if (string.IsNullOrWhiteSpace(qrValue))
        {
            throw new ArgumentException("QR value is required.", nameof(value));
        }

        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(qrValue, QRCodeGenerator.ECCLevel.Q);
        var svgQrCode = new SvgQRCode(qrCodeData);
        return svgQrCode.GetGraphic(10);
    }

    public byte[] GeneratePdfDocument(IReadOnlyList<string> values, QrCodeBatchRequestModel request)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(request);

        if (values.Count == 0)
        {
            throw new ArgumentException("At least one QR value is required.", nameof(values));
        }

        try
        {
            const decimal pageWidth = 595.28m;
            const decimal pageHeight = 841.89m;
            const decimal pageMargin = 24m;
            const decimal millimeterToPoint = 72m / 25.4m;

            var qrWidth = request.QrWidth * millimeterToPoint;
            var qrHeight = request.QrHeight * millimeterToPoint;
            var marginLeft = request.MarginLeft * millimeterToPoint;
            var marginRight = request.MarginRight * millimeterToPoint;
            var marginTop = request.MarginTop * millimeterToPoint;
            var marginBottom = request.MarginBottom * millimeterToPoint;
            var columns = Math.Max(1, request.QrPerRow);
            var cellWidth = qrWidth + marginLeft + marginRight;
            var cellHeight = qrHeight + marginTop + marginBottom;
            var rowsPerPage = Math.Max(1, (int)Math.Floor((pageHeight - (pageMargin * 2)) / cellHeight));
            var itemsPerPage = Math.Max(1, rowsPerPage * columns);
            var pageCount = (int)Math.Ceiling(values.Count / (decimal)itemsPerPage);

            var contentStreams = new List<string>(pageCount);
            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                var pageBuilder = new StringBuilder();
                var pageStart = pageIndex * itemsPerPage;
                var pageEnd = Math.Min(pageStart + itemsPerPage, values.Count);
                var rowsOnPage = (int)Math.Ceiling((pageEnd - pageStart) / (decimal)columns);
                var gridWidth = columns * cellWidth;
                var gridHeight = rowsOnPage * cellHeight;
                var startX = Math.Max(pageMargin, (pageWidth - gridWidth) / 2m);
                var startY = pageHeight - Math.Max(pageMargin, (pageHeight - gridHeight) / 2m);

                for (var itemIndex = pageStart; itemIndex < pageEnd; itemIndex++)
                {
                    var indexWithinPage = itemIndex - pageStart;
                    var row = indexWithinPage / columns;
                    var column = indexWithinPage % columns;
                    var cellX = startX + (column * cellWidth);
                    var cellTopY = startY - (row * cellHeight);

                    DrawQrCode(pageBuilder, values[itemIndex], cellX + marginLeft, cellTopY - marginTop, qrWidth, qrHeight);

                    if (request.ShowDashedLines)
                    {
                        var hasRightSeparator = column < columns - 1 && itemIndex + 1 < pageEnd;
                        var hasBottomSeparator = row < rowsOnPage - 1 && itemIndex + columns < pageEnd;

                        if (hasRightSeparator)
                        {
                            DrawDashedLine(
                                pageBuilder,
                                cellX + cellWidth,
                                cellTopY - 8m,
                                cellX + cellWidth,
                                cellTopY - cellHeight + 8m);
                        }

                        if (hasBottomSeparator)
                        {
                            DrawDashedLine(
                                pageBuilder,
                                cellX + 8m,
                                cellTopY - cellHeight,
                                cellX + cellWidth - 8m,
                                cellTopY - cellHeight);
                        }
                    }
                }

                contentStreams.Add(pageBuilder.ToString());
            }

            return BuildPdfDocument(contentStreams, pageWidth, pageHeight);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to generate QR PDF. ValuesCount={ValuesCount}, QrPerRow={QrPerRow}, QrWidth={QrWidth}, QrHeight={QrHeight}",
                values.Count,
                request.QrPerRow,
                request.QrWidth,
                request.QrHeight);
            throw;
        }
    }

    public byte[] Generate180LabelSheetPdfDocument(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0)
        {
            throw new ArgumentException("At least one QR value is required.", nameof(values));
        }

        try
        {
            const decimal millimeterToPoint = 72m / 25.4m;
            const int columns = 10;
            const int rows = 18;
            const int itemsPerPage = columns * rows;

            var pageWidth = 210m * millimeterToPoint;
            var pageHeight = 297m * millimeterToPoint;
            var marginLeft = 6m * millimeterToPoint;
            var marginTop = 14m * millimeterToPoint;
            var cellWidth = 20m * millimeterToPoint;
            var cellHeight = 15m * millimeterToPoint;
            var qrSize = 14.5m * millimeterToPoint;
            var qrOffsetX = (cellWidth - qrSize) / 2m;
            var qrOffsetY = (cellHeight - qrSize) / 2m;
            var pageCount = (int)Math.Ceiling(values.Count / (decimal)itemsPerPage);

            var contentStreams = new List<string>(pageCount);
            for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                var pageBuilder = new StringBuilder();
                var pageStart = pageIndex * itemsPerPage;
                var pageEnd = Math.Min(pageStart + itemsPerPage, values.Count);

                for (var itemIndex = pageStart; itemIndex < pageEnd; itemIndex++)
                {
                    var indexWithinPage = itemIndex - pageStart;
                    var row = indexWithinPage / columns;
                    var column = indexWithinPage % columns;
                    var cellX = marginLeft + (column * cellWidth);
                    var cellTopY = pageHeight - marginTop - (row * cellHeight);

                    DrawQrCode(
                        pageBuilder,
                        values[itemIndex],
                        cellX + qrOffsetX,
                        cellTopY - qrOffsetY,
                        qrSize,
                        qrSize);
                }

                contentStreams.Add(pageBuilder.ToString());
            }

            return BuildPdfDocument(contentStreams, pageWidth, pageHeight);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate 180-label QR PDF. ValuesCount={ValuesCount}", values.Count);
            throw;
        }
    }

    private async Task<(long FirstSequence, long LastSequence)> ReserveSequenceRangeAsync(
        int quantity,
        CancellationToken cancellationToken)
    {
        await SequenceLock.WaitAsync(cancellationToken);

        try
        {
            var sequenceFilePath = GetSequenceFilePath();
            var directoryPath = Path.GetDirectoryName(sequenceFilePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            long persistedSequence = 0;
            if (File.Exists(sequenceFilePath))
            {
                var fileContents = await File.ReadAllTextAsync(sequenceFilePath, cancellationToken);
                if (!string.IsNullOrWhiteSpace(fileContents) &&
                    long.TryParse(fileContents, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSequence))
                {
                    persistedSequence = parsedSequence;
                }
                else if (!string.IsNullOrWhiteSpace(fileContents))
                {
                    _logger.LogWarning(
                        "Sequence file contains invalid data and will be reset. SequenceFilePath={SequenceFilePath}, RawValue={RawValue}",
                        sequenceFilePath,
                        fileContents);
                }
            }

            var currentSequenceBase = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var firstSequence = Math.Max(currentSequenceBase, persistedSequence + 1);
            var lastSequence = checked(firstSequence + quantity - 1L);

            var maxSupportedSequence = MaxSupportedSequence();
            if (lastSequence > maxSupportedSequence)
            {
                throw new InvalidOperationException("Đã vượt quá giới hạn dải mã QR 9 ký tự.");
            }

            await File.WriteAllTextAsync(
                sequenceFilePath,
                lastSequence.ToString(CultureInfo.InvariantCulture),
                cancellationToken);

            return (firstSequence, lastSequence);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Write access denied for QR sequence file {SequenceFilePath}.", GetSequenceFilePath());
            throw new InvalidOperationException(
                $"Không thể ghi file cấp phát QR tại '{GetSequenceFilePath()}'. Hãy cấp quyền ghi cho App Pool IIS trên thư mục App_Data.",
                ex);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "I/O failure while reading or writing QR sequence file {SequenceFilePath}.", GetSequenceFilePath());
            throw new InvalidOperationException(
                $"Không thể đọc hoặc ghi file cấp phát QR tại '{GetSequenceFilePath()}'. Hãy kiểm tra thư mục App_Data, quyền ghi và trạng thái khóa file.",
                ex);
        }
        finally
        {
            SequenceLock.Release();
        }
    }

    private string BuildQrValue(long sequence)
    {
        var encodedValue = EncodeBase36(sequence).PadLeft(CodeLength, '0');
        return $"{Prefix}{encodedValue}";
    }

    private static string EncodeBase36(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        if (value == 0)
        {
            return "0";
        }

        Span<char> buffer = stackalloc char[CodeLength + 4];
        var position = buffer.Length;
        var remaining = value;

        while (remaining > 0)
        {
            var remainder = (int)(remaining % Alphabet.Length);
            buffer[--position] = Alphabet[remainder];
            remaining /= Alphabet.Length;
        }

        return new string(buffer[position..]);
    }

    private static long MaxSupportedSequence()
    {
        long value = 1;
        for (var index = 0; index < CodeLength; index++)
        {
            value *= Alphabet.Length;
        }

        return value - 1;
    }

    private string GetSequenceFilePath()
    {
        return Path.Combine(_webHostEnvironment.ContentRootPath, "App_Data", "qr-sequence.txt");
    }

    private static void DrawQrCode(
        StringBuilder builder,
        string value,
        decimal left,
        decimal top,
        decimal width,
        decimal height)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(value, QRCodeGenerator.ECCLevel.Q);

        var matrix = qrCodeData.ModuleMatrix;
        var moduleCount = matrix.Count;
        if (moduleCount == 0)
        {
            return;
        }

        var moduleSize = Math.Min(width / moduleCount, height / moduleCount);
        var renderWidth = moduleSize * moduleCount;
        var renderHeight = moduleSize * moduleCount;
        var offsetX = left + ((width - renderWidth) / 2m);
        var offsetY = top - ((height - renderHeight) / 2m);

        builder.AppendLine("0 0 0 rg");
        for (var row = 0; row < moduleCount; row++)
        {
            var rowData = matrix[row];
            for (var column = 0; column < moduleCount; column++)
            {
                if (!rowData[column])
                {
                    continue;
                }

                var x = offsetX + (column * moduleSize);
                var y = offsetY - ((row + 1) * moduleSize);
                builder.AppendLine(FormattableString.Invariant($"{x:0.###} {y:0.###} {moduleSize:0.###} {moduleSize:0.###} re f"));
            }
        }
    }

    private static void DrawDashedLine(
        StringBuilder builder,
        decimal startX,
        decimal startY,
        decimal endX,
        decimal endY)
    {
        builder.AppendLine("[3 3] 0 d");
        builder.AppendLine("0.45 w");
        builder.AppendLine("0.55 G");
        builder.AppendLine(FormattableString.Invariant($"{startX:0.###} {startY:0.###} m {endX:0.###} {endY:0.###} l S"));
        builder.AppendLine("[] 0 d");
        builder.AppendLine("0 G");
    }

    private static byte[] BuildPdfDocument(
        IReadOnlyList<string> contentStreams,
        decimal pageWidth,
        decimal pageHeight)
    {
        var objects = new List<string>();
        var pageObjectNumbers = new List<int>(contentStreams.Count);
        var contentObjectNumbers = new List<int>(contentStreams.Count);

        objects.Add("<< /Type /Catalog /Pages 2 0 R >>");
        objects.Add(string.Empty);
        for (var index = 0; index < contentStreams.Count; index++)
        {
            var pageObjectNumber = objects.Count + 1;
            var contentObjectNumber = objects.Count + 2;
            pageObjectNumbers.Add(pageObjectNumber);
            contentObjectNumbers.Add(contentObjectNumber);

            objects.Add(FormattableString.Invariant(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageWidth:0.###} {pageHeight:0.###}] /Contents {contentObjectNumber} 0 R >>"));

            var streamBytes = Encoding.ASCII.GetBytes(contentStreams[index]);
            objects.Add(FormattableString.Invariant(
                $"<< /Length {streamBytes.Length} >>\nstream\n{contentStreams[index]}endstream"));
        }

        objects[1] = $"<< /Type /Pages /Kids [{string.Join(" ", pageObjectNumbers.Select(number => $"{number} 0 R"))}] /Count {pageObjectNumbers.Count} >>";

        using var memoryStream = new MemoryStream();
        using var writer = new StreamWriter(memoryStream, new UTF8Encoding(false), leaveOpen: true);
        writer.NewLine = "\n";
        writer.WriteLine("%PDF-1.4");
        writer.Flush();

        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(memoryStream.Position);
            writer.WriteLine($"{index + 1} 0 obj");
            writer.WriteLine(objects[index]);
            writer.WriteLine("endobj");
            writer.Flush();
        }

        var xrefPosition = memoryStream.Position;
        writer.WriteLine($"xref\n0 {objects.Count + 1}");
        writer.WriteLine("0000000000 65535 f ");
        for (var index = 1; index < offsets.Count; index++)
        {
            writer.WriteLine($"{offsets[index]:D10} 00000 n ");
        }

        writer.WriteLine($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>");
        writer.WriteLine($"startxref\n{xrefPosition}");
        writer.Write("%%EOF");
        writer.Flush();

        return memoryStream.ToArray();
    }

}
