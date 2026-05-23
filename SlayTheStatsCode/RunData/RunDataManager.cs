using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace SlayTheStats.SlayTheStatsCode.RunData;


public class RunDataManager
{
    private static RunDataManager? _instance;
    

    // TODO: This is very temp, just sketching stuff out
    private Dictionary<SerializableCard, int> numberOfTimesEndedInDeck = new Dictionary<SerializableCard, int>();
    
    private static RunDataManager ConstructDefault()
    {
        RunDataManager runDataManager = new RunDataManager();
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

    public void AddRunToHistory(RunHistory runHistory)
    {
        foreach (SerializableCard card in runHistory.Players[0].Deck)
        {
            numberOfTimesEndedInDeck.TryGetValue(card, out int count);
            numberOfTimesEndedInDeck[card] = count + 1;
        }
    }
    
    public void LoadAllRuns()
    {
        foreach (String name in SaveManager.Instance.GetAllRunHistoryNames())
        {
            ReadSaveResult<RunHistory> readSaveResult = SaveManager.Instance.LoadRunHistory(name);
            if (readSaveResult.Success)
            {
                AddRunToHistory(readSaveResult.SaveData);
                Log.Info($"Loaded run {name}");
            }
            else
            {
                Log.Error($"Could not load run {name}");
            }
        }
    }
}