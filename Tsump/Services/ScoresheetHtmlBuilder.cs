using System.Text;

namespace Tsump.Services;

/// <summary>
/// Generates printable scoresheet HTML for mahjong tables.
/// Reusable across weekly sessions and tournaments.
/// </summary>
public static class ScoresheetHtmlBuilder
{
    public record ScoresheetTable(int TableNumber, int PlayerCount, List<string> PlayerNames, int DefaultStartingPoints = 30000, string? HanchanLabel = null);

    /// <summary>
    /// Builds a complete HTML document containing scoresheets for all tables.
    /// Each table gets a card with player columns and scoring rows to fill in by hand.
    /// Title/subtitle appear in the print header and footer, not in the body.
    /// </summary>
    public static string BuildDocument(
        string title,
        string? subtitle,
        List<ScoresheetTable> tables,
        Func<string, string> lang,
        bool includePrintButton = false)
    {
        var sb = new StringBuilder();
        var headerText = string.IsNullOrEmpty(subtitle) ? title : $"{title} — {subtitle}";

        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.Append($"<title>{headerText}</title>");
        sb.Append("<style>");
        sb.Append(@"
            @page { margin: 10mm 8mm 0mm 8mm; }
            body { font-family: Verdana, Arial, sans-serif; margin: 0; padding: 0; }
            .no-print { position:sticky;top:0;z-index:100;padding:8px;margin-bottom:6px;background:#f0f0f0;border-bottom:1px solid #ccc; }
            .page-header { font-size: 12px; font-weight: bold; margin-bottom: 6px; }
            .page-footer { position: fixed; bottom: 0; left: 0; right: 0; text-align: center;
                           font-size: 10px; color: #666; padding: 2px 0; }
            .sheet { page-break-inside: avoid; break-inside: avoid; }
            .sheet table { width: 100%; border-collapse: collapse; font-size: 12px; table-layout: fixed; }
            .sheet th, .sheet td { border: 1px solid #333; padding: 2px 6px; }
            .sheet th { background: #f5f5f5; }
            .sheet .header-3 { background: #ffc107; color: #000; }
            .sheet .header-4 { background: #198754; color: #000; }
            .sheet .row-label { width: 60px; font-weight: bold; white-space: nowrap; }
            .sheet .score-cell { height: 26px; min-width: 70px; }
            .sheet .score-cell-start { height: 18px; min-width: 70px; }
            .sheet .total-row { background: #e9ecef; font-weight: bold; }
            .sheet .initials-row td { border-color: #aaa; border-bottom: none; height: 22px; }
            .sheet .initials-row .row-label { color: #666; font-weight: normal; }
            .cut-marks { display: flex; justify-content: space-between; align-items: center;
                         margin: 11px 0; font-size: 12px; color: #999; }
            .cut-marks span { letter-spacing: 2px; }
            @media print {
                .no-print { display: none; }
                .page-footer { position: fixed; bottom: 0; }
            }
            @media screen {
                body { padding: 8px; }
                .page-footer { position: static; text-align: center; margin-top: 16px;
                               font-size: 10px; color: #666; border-top: 1px solid #ccc; padding-top: 4px; }
            }
        ");
        sb.Append("</style></head><body>");

        if (includePrintButton)
        {
            sb.Append("<div class=\"no-print\">");
            sb.Append("<button onclick=\"window.print()\" style=\"padding:8px 20px;font-size:16px;cursor:pointer;\">Print</button>");
            sb.Append("</div>");
        }

        // Fixed footer for print
        sb.Append($"<div class=\"page-footer\">{headerText}</div>");

        for (int i = 0; i < tables.Count; i++)
        {
            if (i > 0 && i % 3 != 0)
            {
                sb.Append("<div class=\"cut-marks\"><span>--</span><span>--</span><span>--</span></div>");
            }
            AppendScoresheet(sb, tables[i], lang);
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static void AppendScoresheet(StringBuilder sb, ScoresheetTable table, Func<string, string> lang)
    {
        var headerClass = table.PlayerCount == 3 ? "header-3" : "header-4";
        var hasHanchan = !string.IsNullOrEmpty(table.HanchanLabel);
        var colCount = table.PlayerNames.Count + 1 + (hasHanchan ? 1 : 0);

        // Total content rows: header + names + 7 scoring + 4 separator + initials = 14
        var totalRows = 14;

        sb.Append("<div class=\"sheet\">");
        sb.Append("<table>");

        // Header row with table number (and optional hanchan column)
        sb.Append("<tr>");
        if (hasHanchan)
        {
            sb.Append($"<td rowspan=\"{totalRows}\" style=\"vertical-align:middle;text-align:center;font-size:11px;padding:2px 6px;border:1px solid #333;white-space:nowrap;writing-mode:vertical-rl;transform:rotate(180deg);\">{table.HanchanLabel}</td>");
        }
        sb.Append($"<th colspan=\"{colCount - (hasHanchan ? 1 : 0)}\" class=\"{headerClass}\">");
        sb.Append($"{lang("Table")} {table.TableNumber} ({table.PlayerCount} {lang("players")})");
        sb.Append("</th></tr>");

        // Player names row
        sb.Append("<tr><th class=\"row-label\"></th>");
        foreach (var name in table.PlayerNames)
        {
            sb.Append($"<th style=\"text-align:center;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;\">{name}</th>");
        }
        sb.Append("</tr>");

        // Scoring rows — empty cells for handwriting
        // Order: Start, End, Loan, [+line], Diff, Uma, Penalty, [+line], Total
        string[] rows = ["StartShort", "EndShort", "LoanShort", "DifferenceShort", "UmaShort", "PenaltyShort", "FinalScoreShort"];
        foreach (var rowKey in rows)
        {
            var isTotalRow = rowKey == "FinalScoreShort";
            var isDiffRow = rowKey == "DifferenceShort";
            var isLoanRow = rowKey == "LoanShort";
            var isPenaltyRow = rowKey == "PenaltyShort";
            var isStartRow = rowKey == "StartShort";
            var isEndRow = rowKey == "EndShort";

            // Insert separator line with "+" before Diff and Total rows
            if (isDiffRow || isTotalRow)
            {
                sb.Append($"<tr style=\"height:4px;line-height:0;font-size:0;\"><td colspan=\"{colCount}\" style=\"padding:0;border:none;\"></td></tr>");
                sb.Append("<tr style=\"height:0;line-height:0;font-size:0;\">");
                sb.Append("<td style=\"padding:0 4px;border:none;border-bottom:3px solid #333;font-size:10px;line-height:1;color:#666;vertical-align:bottom;\">+</td>");
                sb.Append($"<td colspan=\"{table.PlayerNames.Count}\" style=\"padding:0;border:none;border-bottom:3px solid #333;\"></td></tr>");
            }

            var trClass = isTotalRow ? " class=\"total-row\"" : "";
            sb.Append($"<tr{trClass}>");
            sb.Append($"<td class=\"row-label\">{lang(rowKey)}</td>");
            var cellClass = isStartRow ? "score-cell-start" : "score-cell";
            for (int i = 0; i < table.PlayerNames.Count; i++)
            {
                if (isStartRow)
                {
                    var display = (table.DefaultStartingPoints / 1000.0).ToString("0.0");
                    sb.Append($"<td class=\"{cellClass}\" style=\"text-align:center;\">{display}</td>");
                }
                else if (isEndRow || isLoanRow || isPenaltyRow)
                    sb.Append($"<td class=\"{cellClass}\" style=\"color:#999;font-size:14px;\">−</td>");
                else
                    sb.Append($"<td class=\"{cellClass}\"></td>");
            }
            sb.Append("</tr>");
        }

        // Initials row
        sb.Append("<tr class=\"initials-row\">");
        sb.Append($"<td class=\"row-label\">{lang("InitialsShort")}</td>");
        for (int i = 0; i < table.PlayerNames.Count; i++)
        {
            sb.Append("<td></td>");
        }
        sb.Append("</tr>");

        sb.Append("</table>");
        sb.Append("</div>");
    }
}
