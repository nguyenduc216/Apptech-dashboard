using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ApptechDashboard.Models;

namespace ApptechDashboard.Services;

public interface ISimpleExcelService
{
    byte[] BuildDonViTinhTemplate();
    IReadOnlyList<DonViTinhImportRow> ReadDonViTinhTemplate(Stream stream);
    byte[] BuildPhongBanTemplate();
    IReadOnlyList<PhongBanImportRow> ReadPhongBanTemplate(Stream stream);
    byte[] BuildCongViecTemplate();
    IReadOnlyList<CongViecImportRow> ReadCongViecTemplate(Stream stream);
    byte[] BuildKhoTemplate();
    IReadOnlyList<KhoImportRow> ReadKhoTemplate(Stream stream);
    byte[] BuildHangHoaTemplate();
    IReadOnlyList<HangHoaImportRow> ReadHangHoaTemplate(Stream stream);
    byte[] BuildHangHoaImportResult(IReadOnlyList<HangHoaImportRowResult> rows);
}

public sealed class SimpleExcelService : ISimpleExcelService
{
    private static readonly XNamespace MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipsNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace OfficeDocumentRelationshipsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    public byte[] BuildDonViTinhTemplate()
    {
        return BuildTemplate("DonViTinh", "TÃªn Ä‘Æ¡n vá»‹", "MÃ£ Ä‘Æ¡n vá»‹");
    }

    public IReadOnlyList<DonViTinhImportRow> ReadDonViTinhTemplate(Stream stream)
    {
        var rows = ReadTemplate(stream, "tendonvi", "madonvi");
        return rows
            .Select(row => new DonViTinhImportRow
            {
                TenDonVi = GetValue(row, "tendonvi") ?? string.Empty,
                MaDonVi = GetValue(row, "madonvi")
            })
            .Where(row => !string.IsNullOrWhiteSpace(row.TenDonVi))
            .ToArray();
    }

    public byte[] BuildPhongBanTemplate()
    {
        return BuildTemplate("PhongBan", "TÃªn phÃ²ng ban", "MÃ£ phÃ²ng ban");
    }

    public IReadOnlyList<PhongBanImportRow> ReadPhongBanTemplate(Stream stream)
    {
        var rows = ReadTemplate(stream, "tenphongban", "maphongban");
        return rows
            .Select(row => new PhongBanImportRow
            {
                TenPhongBan = GetValue(row, "tenphongban") ?? string.Empty,
                MaPhongBan = GetValue(row, "maphongban")
            })
            .Where(row => !string.IsNullOrWhiteSpace(row.TenPhongBan))
            .ToArray();
    }

    public byte[] BuildCongViecTemplate()
    {
        return BuildTemplate(
            "CongViec",
            "TÃªn cÃ´ng viá»‡c",
            "MÃ´ táº£",
            "ÄÆ¡n giÃ¡",
            "Sá»‘ lÆ°á»£ng áº£nh check-in",
            "Sá»‘ lÆ°á»£ng áº£nh check-out");
    }

    public IReadOnlyList<CongViecImportRow> ReadCongViecTemplate(Stream stream)
    {
        var rows = ReadTemplate(
            stream,
            "tencongviec",
            "mota",
            "dongia",
            "soluonganhcheckin",
            "soluonganhcheckout");

        return rows
            .Select(row => new CongViecImportRow
            {
                TenCongViec = GetValue(row, "tencongviec") ?? string.Empty,
                MieuTa = GetValue(row, "mota"),
                DonGia = ParseNullableDecimal(GetValue(row, "dongia")),
                SoLuongAnhCheckIn = ParseNullableInt32(GetValue(row, "soluonganhcheckin")),
                SoLuongAnhCheckOut = ParseNullableInt32(GetValue(row, "soluonganhcheckout"))
            })
            .Where(row => !string.IsNullOrWhiteSpace(row.TenCongViec))
            .ToArray();
    }

    public byte[] BuildKhoTemplate()
    {
        return BuildTemplate("Kho", "TÃªn kho", "MÃ£ kho");
    }

    public IReadOnlyList<KhoImportRow> ReadKhoTemplate(Stream stream)
    {
        var rows = ReadTemplate(stream, "tenkho", "makho");
        return rows
            .Select(row => new KhoImportRow
            {
                TenKho = GetValue(row, "tenkho") ?? string.Empty,
                MaKho = GetValue(row, "makho")
            })
            .Where(row => !string.IsNullOrWhiteSpace(row.TenKho))
            .ToArray();
    }

    public byte[] BuildHangHoaTemplate()
    {
        return BuildTemplate(
            "HangHoa",
            "STT",
            "Ngay nhap",
            "Chi tiet SP",
            "Ten SP",
            "Phan loai",
            "MSP",
            "Ton kho dau ky",
            "DVT",
            "Loai hinh",
            "Ten Kho");
    }

    public IReadOnlyList<HangHoaImportRow> ReadHangHoaTemplate(Stream stream)
    {
        var rows = ReadHangHoaWorkbookRows(stream);
        return rows
            .Select(row => new HangHoaImportRow
            {
                TenHangHoa = GetValue(row.Values, "tensp") ?? GetValue(row.Values, "tenhanghoa") ?? string.Empty,
                MaHangHoa = GetValue(row.Values, "msp") ?? GetValue(row.Values, "mahanghoa"),
                DonViTinh = GetValue(row.Values, "dvt") ?? GetValue(row.Values, "donvitinh"),
                TenKho = GetValue(row.Values, "tenkho"),
                TenPhanLoai = GetValue(row.Values, "phanloai"),
                TenChiTiet = GetValue(row.Values, "chitietsp"),
                TonKhoDauKy = ParseNullableDecimal(GetValue(row.Values, "tonkhodauky")),
                SheetName = row.SheetName,
                RowNumber = row.RowNumber,
                SourceHeaders = row.SourceHeaders,
                SourceValues = row.SourceValues
            })
            .Where(row =>
                !string.IsNullOrWhiteSpace(row.TenHangHoa) ||
                !string.IsNullOrWhiteSpace(row.MaHangHoa) ||
                !string.IsNullOrWhiteSpace(row.TenKho) ||
                !string.IsNullOrWhiteSpace(row.DonViTinh))
            .ToArray();
    }

    public byte[] BuildHangHoaImportResult(IReadOnlyList<HangHoaImportRowResult> rows)
    {
        var sourceHeaders = rows
            .SelectMany(row => row.Row.SourceHeaders)
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourceHeaders.Length == 0)
        {
            sourceHeaders = ["Sheet", "Dong", "Ten SP", "Phan loai", "MSP", "Ton kho dau ky", "DVT", "Ten Kho"];
        }

        var headers = new[] { "Ket qua", "Ghi chu" }.Concat(sourceHeaders).ToArray();
        var values = rows.Select(row =>
        {
            var sourceMap = row.Row.SourceHeaders
                .Select((header, index) => new { header, index })
                .Where(item => !string.IsNullOrWhiteSpace(item.header))
                .GroupBy(item => item.header, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().index, StringComparer.OrdinalIgnoreCase);
            var line = new List<string?> { row.Result, row.Note };
            foreach (var header in sourceHeaders)
            {
                if (sourceMap.TryGetValue(header, out var index) && index < row.Row.SourceValues.Count)
                {
                    line.Add(row.Row.SourceValues[index]);
                    continue;
                }

                line.Add(header switch
                {
                    "Sheet" => row.Row.SheetName,
                    "Dong" => row.Row.RowNumber > 0 ? row.Row.RowNumber.ToString(CultureInfo.InvariantCulture) : null,
                    "Ten SP" => row.Row.TenHangHoa,
                    "Phan loai" => row.Row.TenPhanLoai,
                    "MSP" => row.Row.MaHangHoa,
                    "Ton kho dau ky" => row.Row.TonKhoDauKy?.ToString(CultureInfo.InvariantCulture),
                    "DVT" => row.Row.DonViTinh,
                    "Ten Kho" => row.Row.TenKho,
                    _ => null
                });
            }

            return line;
        }).ToArray();

        return BuildWorkbook("KetQuaImportHangHoa", headers, values);
    }

    private static byte[] BuildTemplate(string sheetName, params string[] headers)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            CreateEntry(archive, "[Content_Types].xml", BuildContentTypesXml());
            CreateEntry(archive, "_rels/.rels", BuildRootRelationshipsXml());
            CreateEntry(archive, "xl/workbook.xml", BuildWorkbookXml(sheetName));
            CreateEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationshipsXml());
            CreateEntry(archive, "xl/styles.xml", BuildStylesXml());
            CreateEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheetXml(headers));
        }

        return stream.ToArray();
    }

    private static byte[] BuildWorkbook(
        string sheetName,
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            CreateEntry(archive, "[Content_Types].xml", BuildContentTypesXml());
            CreateEntry(archive, "_rels/.rels", BuildRootRelationshipsXml());
            CreateEntry(archive, "xl/workbook.xml", BuildWorkbookXml(sheetName));
            CreateEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationshipsXml());
            CreateEntry(archive, "xl/styles.xml", BuildStylesXml());
            CreateEntry(archive, "xl/worksheets/sheet1.xml", BuildWorksheetXml(headers, rows));
        }

        return stream.ToArray();
    }

    private static IReadOnlyList<Dictionary<string, string?>> ReadTemplate(Stream stream, params string[] headerKeys)
    {
        return ReadTemplate(stream, headerKeys, null);
    }

    private static IReadOnlyList<HangHoaWorkbookRow> ReadHangHoaWorkbookRows(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var workbook = LoadXml(archive, "xl/workbook.xml");
        var workbookRels = LoadXml(archive, "xl/_rels/workbook.xml.rels");
        var sharedStrings = TryLoadSharedStrings(archive);
        var items = new List<HangHoaWorkbookRow>();

        var sheets = workbook.Root?
            .Element(MainNs + "sheets")?
            .Elements(MainNs + "sheet")
            .ToArray() ?? [];

        foreach (var sheet in sheets)
        {
            var relationshipId = sheet.Attribute(OfficeDocumentRelationshipsNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(relationshipId))
            {
                continue;
            }

            var worksheetPath = workbookRels.Root?
                .Elements(RelationshipsNs + "Relationship")
                .FirstOrDefault(element => string.Equals(element.Attribute("Id")?.Value, relationshipId, StringComparison.Ordinal))
                ?.Attribute("Target")?.Value;

            if (string.IsNullOrWhiteSpace(worksheetPath))
            {
                continue;
            }

            var normalizedWorksheetPath = worksheetPath.Replace("\\", "/", StringComparison.Ordinal);
            if (!normalizedWorksheetPath.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
            {
                normalizedWorksheetPath = $"xl/{normalizedWorksheetPath.TrimStart('/')}";
            }

            var worksheet = LoadXml(archive, normalizedWorksheetPath);
            var rows = worksheet.Root?
                .Element(MainNs + "sheetData")?
                .Elements(MainNs + "row")
                .ToList() ?? [];
            if (rows.Count == 0)
            {
                continue;
            }

            var headerRowState = rows
                .Select(row => new
                {
                    Row = row,
                    Headers = ReadRowValues(row, sharedStrings)
                })
                .Select(item => new
                {
                    item.Row,
                    item.Headers,
                    Map = item.Headers
                        .Select((value, index) => new { value, index })
                        .Where(cell => !string.IsNullOrWhiteSpace(cell.value))
                        .GroupBy(cell => NormalizeHeader(cell.value), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(group => group.Key, group => group.First().index, StringComparer.OrdinalIgnoreCase)
                })
                .FirstOrDefault(item =>
                    item.Map.ContainsKey("msp") &&
                    item.Map.ContainsKey("tensp") &&
                    item.Map.ContainsKey("dvt") &&
                    item.Map.ContainsKey("tenkho") &&
                    item.Map.ContainsKey("tonkhodauky"));

            if (headerRowState is null)
            {
                continue;
            }

            var headerRowNumber = ParseRowNumber(headerRowState.Row);
            var allKeys = new[] { "chitietsp", "tensp", "phanloai", "msp", "tonkhodauky", "dvt", "tenkho" };
            var sourceHeaders = headerRowState.Headers
                .Select(header => string.IsNullOrWhiteSpace(header) ? string.Empty : header.Trim())
                .ToArray();

            foreach (var row in rows.Where(row => ParseRowNumber(row) > headerRowNumber))
            {
                var values = ReadRowValues(row, sharedStrings);
                if (values.All(value => string.IsNullOrWhiteSpace(value)))
                {
                    continue;
                }

                var item = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var key in allKeys)
                {
                    item[key] = headerRowState.Map.TryGetValue(key, out var columnIndex) && columnIndex < values.Count
                        ? values[columnIndex]?.Trim()
                        : null;
                }

                items.Add(new HangHoaWorkbookRow
                {
                    SheetName = sheet.Attribute("name")?.Value,
                    RowNumber = ParseRowNumber(row),
                    Values = item,
                    SourceHeaders = sourceHeaders,
                    SourceValues = Enumerable.Range(0, sourceHeaders.Length)
                        .Select(index => index < values.Count ? values[index]?.Trim() : null)
                        .ToArray()
                });
            }
        }

        return items;
    }

    private static IReadOnlyList<Dictionary<string, string?>> ReadTemplate(
        Stream stream,
        IReadOnlyCollection<string> requiredHeaderKeys,
        IReadOnlyCollection<string>? optionalHeaderKeys)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var workbook = LoadXml(archive, "xl/workbook.xml");
        var workbookRels = LoadXml(archive, "xl/_rels/workbook.xml.rels");
        var sharedStrings = TryLoadSharedStrings(archive);

        var firstSheet = workbook.Root?
            .Element(MainNs + "sheets")?
            .Elements(MainNs + "sheet")
            .FirstOrDefault();

        if (firstSheet is null)
        {
            return [];
        }

        var relationshipId = firstSheet.Attribute(OfficeDocumentRelationshipsNs + "id")?.Value;
        if (string.IsNullOrWhiteSpace(relationshipId))
        {
            return [];
        }

        var worksheetPath = workbookRels.Root?
            .Elements(RelationshipsNs + "Relationship")
            .FirstOrDefault(element => string.Equals(element.Attribute("Id")?.Value, relationshipId, StringComparison.Ordinal))
            ?.Attribute("Target")?.Value;

        if (string.IsNullOrWhiteSpace(worksheetPath))
        {
            return [];
        }

        var normalizedWorksheetPath = worksheetPath.Replace("\\", "/", StringComparison.Ordinal);
        if (!normalizedWorksheetPath.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedWorksheetPath = $"xl/{normalizedWorksheetPath.TrimStart('/')}";
        }

        var worksheet = LoadXml(archive, normalizedWorksheetPath);
        var rows = worksheet.Root?
            .Element(MainNs + "sheetData")?
            .Elements(MainNs + "row")
            .ToList() ?? [];

        if (rows.Count == 0)
        {
            return [];
        }

        var requiredKeys = requiredHeaderKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var optionalKeys = optionalHeaderKeys?
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
        var allKeys = requiredKeys
            .Concat(optionalKeys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var headerMap = ReadRowValues(rows[0], sharedStrings)
            .Select((value, index) => new { value, index })
            .Where(item => !string.IsNullOrWhiteSpace(item.value))
            .ToDictionary(item => NormalizeHeader(item.value), item => item.index, StringComparer.OrdinalIgnoreCase);

        if (requiredKeys.Length == 0 || requiredKeys.Any(key => !headerMap.ContainsKey(key)))
        {
            return [];
        }

        var items = new List<Dictionary<string, string?>>();
        foreach (var row in rows.Skip(1))
        {
            var values = ReadRowValues(row, sharedStrings);
            if (values.All(value => string.IsNullOrWhiteSpace(value)))
            {
                continue;
            }

            var item = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in allKeys)
            {
                if (!headerMap.TryGetValue(key, out var columnIndex))
                {
                    item[key] = null;
                    continue;
                }

                item[key] = columnIndex < values.Count ? values[columnIndex]?.Trim() : null;
            }

            items.Add(item);
        }

        return items;
    }

    private static string BuildContentTypesXml()
    {
        var document = new XDocument(
            new XElement(ContentTypesNs + "Types",
                new XElement(ContentTypesNs + "Default",
                    new XAttribute("Extension", "rels"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(ContentTypesNs + "Default",
                    new XAttribute("Extension", "xml"),
                    new XAttribute("ContentType", "application/xml")),
                new XElement(ContentTypesNs + "Override",
                    new XAttribute("PartName", "/xl/workbook.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                new XElement(ContentTypesNs + "Override",
                    new XAttribute("PartName", "/xl/worksheets/sheet1.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
                new XElement(ContentTypesNs + "Override",
                    new XAttribute("PartName", "/xl/styles.xml"),
                    new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"))));

        return document.DeclarationAwareString();
    }

    private static string BuildRootRelationshipsXml()
    {
        var document = new XDocument(
            new XElement(RelationshipsNs + "Relationships",
                new XElement(RelationshipsNs + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                    new XAttribute("Target", "xl/workbook.xml"))));

        return document.DeclarationAwareString();
    }

    private static string BuildWorkbookXml(string sheetName)
    {
        var document = new XDocument(
            new XElement(MainNs + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", OfficeDocumentRelationshipsNs),
                new XElement(MainNs + "sheets",
                    new XElement(MainNs + "sheet",
                        new XAttribute("name", sheetName),
                        new XAttribute("sheetId", "1"),
                        new XAttribute(OfficeDocumentRelationshipsNs + "id", "rId1")))));

        return document.DeclarationAwareString();
    }

    private static string BuildWorkbookRelationshipsXml()
    {
        var document = new XDocument(
            new XElement(RelationshipsNs + "Relationships",
                new XElement(RelationshipsNs + "Relationship",
                    new XAttribute("Id", "rId1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                    new XAttribute("Target", "worksheets/sheet1.xml")),
                new XElement(RelationshipsNs + "Relationship",
                    new XAttribute("Id", "rId2"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
                    new XAttribute("Target", "styles.xml"))));

        return document.DeclarationAwareString();
    }

    private static string BuildStylesXml()
    {
        var document = new XDocument(
            new XElement(MainNs + "styleSheet",
                new XElement(MainNs + "fonts",
                    new XAttribute("count", "1"),
                    new XElement(MainNs + "font",
                        new XElement(MainNs + "sz", new XAttribute("val", "11")),
                        new XElement(MainNs + "name", new XAttribute("val", "Calibri")))),
                new XElement(MainNs + "fills",
                    new XAttribute("count", "2"),
                    new XElement(MainNs + "fill", new XElement(MainNs + "patternFill", new XAttribute("patternType", "none"))),
                    new XElement(MainNs + "fill", new XElement(MainNs + "patternFill", new XAttribute("patternType", "gray125")))),
                new XElement(MainNs + "borders",
                    new XAttribute("count", "1"),
                    new XElement(MainNs + "border",
                        new XElement(MainNs + "left"),
                        new XElement(MainNs + "right"),
                        new XElement(MainNs + "top"),
                        new XElement(MainNs + "bottom"),
                        new XElement(MainNs + "diagonal"))),
                new XElement(MainNs + "cellStyleXfs",
                    new XAttribute("count", "1"),
                    new XElement(MainNs + "xf",
                        new XAttribute("numFmtId", "0"),
                        new XAttribute("fontId", "0"),
                        new XAttribute("fillId", "0"),
                        new XAttribute("borderId", "0"))),
                new XElement(MainNs + "cellXfs",
                    new XAttribute("count", "1"),
                    new XElement(MainNs + "xf",
                        new XAttribute("numFmtId", "0"),
                        new XAttribute("fontId", "0"),
                        new XAttribute("fillId", "0"),
                        new XAttribute("borderId", "0"),
                        new XAttribute("xfId", "0"))),
                new XElement(MainNs + "cellStyles",
                    new XAttribute("count", "1"),
                    new XElement(MainNs + "cellStyle",
                        new XAttribute("name", "Normal"),
                        new XAttribute("xfId", "0"),
                        new XAttribute("builtinId", "0")))));

        return document.DeclarationAwareString();
    }

    private static string BuildWorksheetXml(params string[] headers)
    {
        var headerValues = headers.Length == 0 ? ["TÃªn"] : headers;
        var headerRow = new XElement(
            MainNs + "row",
            new XAttribute("r", "1"),
            headerValues.Select((value, index) => BuildInlineStringCell($"{GetColumnName(index)}1", value)));

        var columns = new XElement(
            MainNs + "cols",
            headerValues.Select((_, index) => new XElement(
                MainNs + "col",
                new XAttribute("min", index + 1),
                new XAttribute("max", index + 1),
                new XAttribute("width", index == 0 ? "28" : "20"),
                new XAttribute("customWidth", "1"))));

        var document = new XDocument(
            new XElement(MainNs + "worksheet",
                new XElement(MainNs + "sheetViews",
                    new XElement(MainNs + "sheetView", new XAttribute("workbookViewId", "0"))),
                new XElement(MainNs + "sheetFormatPr", new XAttribute("defaultRowHeight", "15")),
                columns,
                new XElement(MainNs + "sheetData", headerRow)));

        return document.DeclarationAwareString();
    }

    private static string BuildWorksheetXml(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        var headerValues = headers.Count == 0 ? ["Ten"] : headers;
        var rowElements = new List<XElement>
        {
            new(
                MainNs + "row",
                new XAttribute("r", "1"),
                headerValues.Select((value, index) => BuildInlineStringCell($"{GetColumnName(index)}1", value)))
        };

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var rowNumber = rowIndex + 2;
            rowElements.Add(new XElement(
                MainNs + "row",
                new XAttribute("r", rowNumber.ToString(CultureInfo.InvariantCulture)),
                rows[rowIndex].Select((value, columnIndex) => BuildInlineStringCell(
                    $"{GetColumnName(columnIndex)}{rowNumber}",
                    value ?? string.Empty))));
        }

        var columns = new XElement(
            MainNs + "cols",
            headerValues.Select((_, index) => new XElement(
                MainNs + "col",
                new XAttribute("min", index + 1),
                new XAttribute("max", index + 1),
                new XAttribute("width", index == 0 ? "18" : index == 1 ? "42" : "20"),
                new XAttribute("customWidth", "1"))));

        var document = new XDocument(
            new XElement(MainNs + "worksheet",
                new XElement(MainNs + "sheetViews",
                    new XElement(MainNs + "sheetView", new XAttribute("workbookViewId", "0"))),
                new XElement(MainNs + "sheetFormatPr", new XAttribute("defaultRowHeight", "15")),
                columns,
                new XElement(MainNs + "sheetData", rowElements)));

        return document.DeclarationAwareString();
    }

    private static XElement BuildInlineStringCell(string reference, string value)
    {
        return new XElement(MainNs + "c",
            new XAttribute("r", reference),
            new XAttribute("t", "inlineStr"),
            new XElement(MainNs + "is", new XElement(MainNs + "t", value)));
    }

    private static XDocument LoadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path)
            ?? throw new InvalidDataException($"KhÃ´ng tÃ¬m tháº¥y entry {path} trong file Excel.");

        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string[]? TryLoadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document.Root?
            .Elements(MainNs + "si")
            .Select(item => string.Concat(item.Descendants(MainNs + "t").Select(text => text.Value)))
            .ToArray();
    }

    private static List<string?> ReadRowValues(XElement row, string[]? sharedStrings)
    {
        var values = new Dictionary<int, string?>();
        foreach (var cell in row.Elements(MainNs + "c"))
        {
            var reference = cell.Attribute("r")?.Value;
            if (string.IsNullOrWhiteSpace(reference))
            {
                continue;
            }

            var columnIndex = GetColumnIndex(reference);
            values[columnIndex] = GetCellValue(cell, sharedStrings);
        }

        if (values.Count == 0)
        {
            return [];
        }

        var maxIndex = values.Keys.Max();
        var items = new List<string?>(Enumerable.Repeat<string?>(null, maxIndex + 1));
        foreach (var item in values)
        {
            items[item.Key] = item.Value;
        }

        return items;
    }

    private static int ParseRowNumber(XElement row)
    {
        return int.TryParse(row.Attribute("r")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowNumber)
            ? rowNumber
            : 0;
    }

    private static string? GetCellValue(XElement cell, string[]? sharedStrings)
    {
        var cellType = cell.Attribute("t")?.Value;

        if (string.Equals(cellType, "inlineStr", StringComparison.OrdinalIgnoreCase))
        {
            return string.Concat(cell.Descendants(MainNs + "t").Select(text => text.Value));
        }

        var rawValue = cell.Element(MainNs + "v")?.Value;
        if (rawValue is null)
        {
            return null;
        }

        if (string.Equals(cellType, "s", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedStringIndex) &&
            sharedStrings is not null &&
            sharedStringIndex >= 0 &&
            sharedStringIndex < sharedStrings.Length)
        {
            return sharedStrings[sharedStringIndex];
        }

        return rawValue;
    }

    private static int GetColumnIndex(string cellReference)
    {
        var letters = new string(cellReference.TakeWhile(char.IsLetter).ToArray()).ToUpperInvariant();
        var index = 0;
        foreach (var letter in letters)
        {
            index = (index * 26) + (letter - 'A' + 1);
        }

        return Math.Max(0, index - 1);
    }

    private static string GetColumnName(int index)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var builder = new StringBuilder();
        var value = index;

        do
        {
            builder.Insert(0, alphabet[value % 26]);
            value = (value / 26) - 1;
        }
        while (value >= 0);

        return builder.ToString();
    }

    private static string NormalizeHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (character is 'Đ' or 'đ')
            {
                builder.Append('d');
                continue;
            }

            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static string? GetValue(IReadOnlyDictionary<string, string?> row, string key)
    {
        return row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static decimal? ParseNullableDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariantValue))
        {
            return invariantValue;
        }

        if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var currentCultureValue))
        {
            return currentCultureValue;
        }

        var normalized = value.Replace(",", string.Empty, StringComparison.Ordinal);
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var sanitizedValue)
            ? sanitizedValue
            : null;
    }

    private static int? ParseNullableInt32(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var invariantValue))
        {
            return invariantValue;
        }

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out var currentCultureValue))
        {
            return currentCultureValue;
        }

        var normalized = value.Replace(",", string.Empty, StringComparison.Ordinal);
        return int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sanitizedValue)
            ? sanitizedValue
            : null;
    }

    private static void CreateEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private sealed class HangHoaWorkbookRow
    {
        public string? SheetName { get; init; }
        public int RowNumber { get; init; }
        public IReadOnlyDictionary<string, string?> Values { get; init; } = new Dictionary<string, string?>();
        public IReadOnlyList<string> SourceHeaders { get; init; } = [];
        public IReadOnlyList<string?> SourceValues { get; init; } = [];
    }
}

internal static class XDocumentExtensions
{
    public static string DeclarationAwareString(this XDocument document)
    {
        using var writer = new Utf8StringWriter();
        document.Save(writer);
        return writer.ToString();
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }
}
