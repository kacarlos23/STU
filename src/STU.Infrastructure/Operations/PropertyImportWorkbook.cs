using System.Globalization;
using ClosedXML.Excel;

namespace STU.Infrastructure.Operations;

public static class PropertyImportWorkbook
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string FileName = "modelo-importacao-imoveis.xlsx";

    public static IReadOnlyList<PropertyImportColumn> Columns { get; } =
    [
        new("microregionCode", "Código da microrregião", "Obrigatório", "MR01"),
        new("street", "Logradouro do imóvel", "Obrigatório", "Rua das Flores"),
        new("houseNumber", "Número do imóvel", "Obrigatório", "120"),
        new("familyNumber", "Número da família", "Opcional; preencher junto com o responsável", "F-001"),
        new("familyResponsibleName", "Nome do responsável pela família", "Opcional; preencher junto com o número da família", "Maria da Silva"),
        new("postalCode", "CEP", "Opcional", "45990-000"),
        new("complement", "Complemento", "Opcional", "Casa B"),
        new("longitude", "Longitude em graus decimais", "Obrigatório", "-39.7419"),
        new("latitude", "Latitude em graus decimais", "Obrigatório", "-17.5394"),
        new("registrationStatus", "Situação cadastral", "Opcional; Active ou Draft", "Active"),
        new("situation", "Situação do imóvel", "Opcional; Occupied, Vacant, Abandoned ou Demolished", "Occupied"),
    ];

    public static byte[] CreateTemplate()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Imóveis");

        for (var index = 0; index < Columns.Count; index++)
        {
            var column = Columns[index];
            var cell = sheet.Cell(1, index + 1);
            cell.Value = column.Name;
            cell.GetComment().AddText($"{column.Description}. {column.Requirement}. Exemplo: {column.Example}.");
        }

        var inputRange = sheet.Range(1, 1, 2, Columns.Count);
        var table = inputRange.CreateTable("ImportacaoImoveis");
        table.Theme = XLTableTheme.TableStyleMedium4;
        table.ShowAutoFilter = true;
        table.ShowRowStripes = true;

        var header = sheet.Range(1, 1, 1, Columns.Count);
        header.Style.Font.Bold = true;
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        header.Style.Alignment.WrapText = true;
        sheet.Row(1).Height = 32;
        sheet.SheetView.FreezeRows(1);
        sheet.Range(2, 1, 10001, Columns.Count).Style.NumberFormat.Format = "@";

        var widths = new[] { 20d, 30d, 16d, 18d, 34d, 16d, 24d, 18d, 18d, 22d, 20d };
        for (var index = 0; index < widths.Length; index++) sheet.Column(index + 1).Width = widths[index];

        var instructions = workbook.Worksheets.Add("Instruções");
        instructions.Cell("A1").Value = "Como preencher a planilha";
        instructions.Cell("A1").Style.Font.Bold = true;
        instructions.Cell("A1").Style.Font.FontSize = 16;
        instructions.Cell("A3").Value = "Preencha uma linha por imóvel na aba Imóveis e não altere os nomes das colunas.";
        instructions.Cell("A4").Value = "Número e responsável da família devem ser preenchidos juntos ou permanecer ambos vazios.";
        instructions.Cell("A5").Value = "Use ponto ou vírgula como separador decimal para longitude e latitude.";
        instructions.Cell("A7").Value = "Coluna";
        instructions.Cell("B7").Value = "Descrição";
        instructions.Cell("C7").Value = "Preenchimento";
        instructions.Cell("D7").Value = "Exemplo";
        for (var index = 0; index < Columns.Count; index++)
        {
            var row = index + 8;
            var column = Columns[index];
            instructions.Cell(row, 1).Value = column.Name;
            instructions.Cell(row, 2).Value = column.Description;
            instructions.Cell(row, 3).Value = column.Requirement;
            instructions.Cell(row, 4).Value = column.Example;
        }
        var guideTable = instructions.Range(7, 1, Columns.Count + 7, 4).CreateTable("InstrucoesImportacao");
        guideTable.Theme = XLTableTheme.TableStyleMedium4;
        instructions.SheetView.FreezeRows(7);
        instructions.Columns(1, 4).AdjustToContents(7, Columns.Count + 7);
        instructions.Column(2).Width = Math.Min(instructions.Column(2).Width, 42);
        instructions.Column(3).Width = Math.Min(instructions.Column(3).Width, 48);
        instructions.Rows(3, 5).Style.Alignment.WrapText = true;
        instructions.Range(7, 1, Columns.Count + 7, 4).Style.Alignment.WrapText = true;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static IReadOnlyList<IReadOnlyDictionary<string, string>> ReadRows(string path)
    {
        using var workbook = new XLWorkbook(path);
        if (!workbook.TryGetWorksheet("Imóveis", out var sheet)) sheet = workbook.Worksheets.First();
        var headerRow = sheet.FirstRowUsed() ?? throw new InvalidDataException("A planilha não contém cabeçalho.");
        var lastHeaderCell = headerRow.LastCellUsed() ?? throw new InvalidDataException("A planilha não contém colunas.");
        var headers = Enumerable.Range(1, lastHeaderCell.Address.ColumnNumber)
            .Select(column => CellText(headerRow.Cell(column)).Trim().ToLowerInvariant())
            .ToArray();
        if (headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
            throw new InvalidDataException("A planilha contém colunas repetidas.");

        var rows = new List<IReadOnlyDictionary<string, string>>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow.RowNumber();
        for (var rowNumber = headerRow.RowNumber() + 1; rowNumber <= lastRow; rowNumber++)
        {
            var row = sheet.Row(rowNumber);
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var hasValue = false;
            for (var column = 1; column <= headers.Length; column++)
            {
                if (string.IsNullOrWhiteSpace(headers[column - 1])) continue;
                var value = CellText(row.Cell(column)).Trim();
                values[headers[column - 1]] = value;
                hasValue |= value.Length > 0;
            }
            if (hasValue) rows.Add(values);
        }
        return rows;
    }

    private static string CellText(IXLCell cell)
    {
        if (cell.HasFormula) throw new InvalidDataException("A planilha de importação não aceita fórmulas.");
        if (cell.IsEmpty()) return string.Empty;
        return cell.DataType switch
        {
            XLDataType.Number => cell.GetDouble().ToString(CultureInfo.InvariantCulture),
            XLDataType.DateTime => cell.GetDateTime().ToString("O", CultureInfo.InvariantCulture),
            XLDataType.Boolean => cell.GetBoolean() ? "true" : "false",
            _ => cell.GetString(),
        };
    }
}

public sealed record PropertyImportColumn(string Name, string Description, string Requirement, string Example);
