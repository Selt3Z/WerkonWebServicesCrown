
namespace Crown.Contracts.Import;

public sealed class SpreadsheetImportCommitResponse
{
    public int Created { get; set; }

    public int Skipped { get; set; }

    public int Errors { get; set; }

    public List<SpreadsheetImportRowError> ErrorRows { get; set; } = [];
}
