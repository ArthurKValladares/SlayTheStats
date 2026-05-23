using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace SlayTheStats.SlayTheStatsCode.RunData;


public class RunData
{
    private static RunData? _instance;
    
    private readonly List<string> _runNames = new List<string>();

    private static RunData ConstructDefault()
    {
        RunData runData = new RunData();
        runData._runNames.Clear();
        runData._runNames.AddRange((IEnumerable<string>) SaveManager.Instance.GetAllRunHistoryNames());
        runData._runNames.Reverse();
        return runData;
    }

    public static RunData Instance
    {
        get
        {
            RunData._instance ??= RunData.ConstructDefault();
            return RunData._instance;
        }
    }

    public void TryLoadTest()
    {
        foreach (String name in _runNames)
        {
            ReadSaveResult<RunHistory> readSaveResult = SaveManager.Instance.LoadRunHistory(name);
            if (readSaveResult.Success)
            {
                Log.Info($"Loaded run {name}");
            }
            else
            {
                Log.Error($"Could not load run {name}");
            }
        }
    }
}