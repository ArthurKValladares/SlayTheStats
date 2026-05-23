using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace SlayTheStats.SlayTheStatsCode.RunData;


public class RunDataManager
{
    private static RunDataManager? _instance;
    
    private readonly List<string> _runNames = new List<string>();

    private static RunDataManager ConstructDefault()
    {
        RunDataManager runDataManager = new RunDataManager();
        runDataManager._runNames.Clear();
        runDataManager._runNames.AddRange((IEnumerable<string>) SaveManager.Instance.GetAllRunHistoryNames());
        runDataManager._runNames.Reverse();
        return runDataManager;
    }

    public static RunDataManager Instance
    {
        get
        {
            RunDataManager._instance ??= RunDataManager.ConstructDefault();
            return RunDataManager._instance;
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