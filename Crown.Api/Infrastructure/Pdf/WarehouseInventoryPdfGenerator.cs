using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Crown.Infrastructure.Pdf;

public sealed class WarehouseInventoryPdfGenerator
{
    private static readonly Color BrandBlue = Color.FromHex("#2765A8");
    private static readonly Color SoftGray = Color.FromHex("#F3F5F8");
    private static readonly Color BorderGray = Color.FromHex("#C9D0D8");
    private static readonly Color LabelGray = Color.FromHex("#5B6673");
    private static readonly Color ShortageRed = Color.FromHex("#B42318");

    public byte[] Generate(WarehouseInventoryPdfData data)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var fonts = PdfFontRegistry.GetFontPair(data.Locale.CultureName);

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(style => style
                    .FontSize(8f)
                    .FontFamily(fonts.Primary, fonts.Fallback)
                    .FontColor(Colors.Black));

                page.Header().Element(header => ComposeHeader(header, data));
                page.Content().Element(content => ComposeContent(content, data));
                page.Footer().Element(footer => ComposeFooter(footer, data));
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, WarehouseInventoryPdfData data)
    {
        var locale = data.Locale;

        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    if (!string.IsNullOrWhiteSpace(data.Organization.OrganizationName))
                    {
                        left.Item().Text(data.Organization.OrganizationName).Bold().FontSize(11).FontColor(BrandBlue);
                    }

                    if (!string.IsNullOrWhiteSpace(data.Organization.Address))
                    {
                        left.Item().PaddingTop(2).Text(data.Organization.Address).FontSize(7.5f).FontColor(LabelGray);
                    }
                });

                if (data.Organization.LogoBytes is { Length: > 0 })
                {
                    row.ConstantItem(96).AlignRight().AlignMiddle().Height(46).Image(data.Organization.LogoBytes).FitArea();
                }
            });

            column.Item().PaddingTop(8).LineHorizontal(1.2f).LineColor(BrandBlue);

            column.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem().AlignMiddle().Text(locale.T("Inventory_Title")).Bold().FontSize(13).FontColor(BrandBlue);
                row.ConstantItem(180).AlignRight().Text($"{locale.T("Pdf_Date")} {data.GeneratedAtLocal:dd.MM.yyyy HH:mm}").FontSize(8);
            });

            if (!string.IsNullOrWhiteSpace(data.FilterCaption))
            {
                column.Item().PaddingTop(4).Text(data.FilterCaption).FontSize(7.5f).FontColor(LabelGray);
            }
        });
    }

    private static void ComposeContent(IContainer container, WarehouseInventoryPdfData data)
    {
        var locale = data.Locale;

        container.PaddingTop(10).Column(column =>
        {
            if (data.Rows.Count == 0)
            {
                column.Item().Text(locale.T("Inventory_Empty")).FontColor(LabelGray);
                return;
            }

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2.4f);
                    columns.RelativeColumn(1.1f);
                    columns.RelativeColumn(1.1f);
                    columns.RelativeColumn(1.1f);
                    columns.ConstantColumn(36);
                    columns.ConstantColumn(42);
                    columns.ConstantColumn(42);
                    columns.ConstantColumn(52);
                });

                table.Header(header =>
                {
                    header.Cell().Element(CellHeader).Text(locale.T("Pdf_ItemName"));
                    header.Cell().Element(CellHeader).Text(locale.T("Inventory_Code"));
                    header.Cell().Element(CellHeader).Text(locale.T("Inventory_Barcode"));
                    header.Cell().Element(CellHeader).Text(locale.T("Inventory_Category"));
                    header.Cell().Element(CellHeader).AlignCenter().Text(locale.T("Inventory_Unit"));
                    header.Cell().Element(CellHeader).AlignRight().Text(locale.T("Inventory_Qty"));
                    header.Cell().Element(CellHeader).AlignRight().Text(locale.T("Inventory_MinQty"));
                    header.Cell().Element(CellHeader).AlignRight().Text(locale.T("Pdf_UnitPrice"));
                });

                var index = 0;
                foreach (var row in data.Rows)
                {
                    var bg = index % 2 == 0 ? Colors.White : SoftGray;
                    var low = row.QuantityOnHand <= row.MinQuantityOnHand;
                    index++;

                    table.Cell().Element(c => CellBody(c, bg)).Text(row.Name + (row.IsArchived ? $" ({locale.T("Inventory_Archived")})" : string.Empty));
                    table.Cell().Element(c => CellBody(c, bg)).Text(row.Code ?? locale.T("Pdf_Empty"));
                    table.Cell().Element(c => CellBody(c, bg)).Text(row.Barcode ?? locale.T("Pdf_Empty"));
                    table.Cell().Element(c => CellBody(c, bg)).Text(row.Category ?? locale.T("Pdf_Empty"));
                    table.Cell().Element(c => CellBody(c, bg)).AlignCenter().Text(row.Unit);
                    table.Cell().Element(c => CellBody(c, bg))
                        .AlignRight()
                        .Text(FormatQty(row.QuantityOnHand))
                        .FontColor(low ? ShortageRed : Colors.Black);
                    table.Cell().Element(c => CellBody(c, bg)).AlignRight().Text(FormatQty(row.MinQuantityOnHand));
                    table.Cell().Element(c => CellBody(c, bg)).AlignRight().Text(locale.FormatMoney(row.UnitPrice));
                }
            });

            column.Item().PaddingTop(8).Text($"{locale.T("Inventory_TotalRows")}: {data.Rows.Count}").FontSize(8).FontColor(LabelGray);
        });
    }

    private static void ComposeFooter(IContainer container, WarehouseInventoryPdfData data)
    {
        container.AlignCenter().Text(text =>
        {
            text.Span($"{data.Locale.T("Pdf_GeneratedAt")} {data.GeneratedAtLocal:dd.MM.yyyy HH:mm} · ")
                .FontSize(7).FontColor(LabelGray);
            text.CurrentPageNumber().FontSize(7).FontColor(LabelGray);
            text.Span(" / ").FontSize(7).FontColor(LabelGray);
            text.TotalPages().FontSize(7).FontColor(LabelGray);
        });
    }

    private static IContainer CellHeader(IContainer container) =>
        container
            .BorderBottom(1)
            .BorderColor(BorderGray)
            .Background(SoftGray)
            .PaddingVertical(4)
            .PaddingHorizontal(3)
            .DefaultTextStyle(x => x.SemiBold().FontSize(7));

    private static IContainer CellBody(IContainer container, Color background) =>
        container
            .Background(background)
            .BorderBottom(0.5f)
            .BorderColor(BorderGray)
            .PaddingVertical(3)
            .PaddingHorizontal(3);

    private static string FormatQty(decimal qty) =>
        qty == decimal.Truncate(qty)
            ? ((long)qty).ToString()
            : qty.ToString("0.#");
}
