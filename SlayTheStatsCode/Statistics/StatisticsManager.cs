namespace SlayTheStats.SlayTheStatsCode.Statistics;

public class StatisticsManager
{
    private static StatisticsManager? _instance;
    
    private static StatisticsManager ConstructDefault()
    {
        StatisticsManager runDataManager = new StatisticsManager();
        return runDataManager;
    }

    public static StatisticsManager Instance
    {
        get
        {
            StatisticsManager._instance ??= StatisticsManager.ConstructDefault();
            return StatisticsManager._instance;
        }
    }
}