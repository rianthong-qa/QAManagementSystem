namespace Promaxx2.Automation.Data;

/// <summary>
/// Phase 2: snapshot/copy FBMAXX2.FDB ต่อ run + restore + SQL seed
/// ต้องตัดสินใจ Test Data Channel (AUTOMATION_PLAN.md §6.5) ก่อน implement จริง
/// </summary>
public sealed class FdbManager
{
    public FdbManager(string fdbPath)
    {
        if (string.IsNullOrWhiteSpace(fdbPath))
            throw new ArgumentException("FdbPath is required (env AUT_FDB_PATH).");
        FdbPath = fdbPath;
    }

    public string FdbPath { get; }
}
