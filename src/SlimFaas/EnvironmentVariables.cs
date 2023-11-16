namespace SlimFaas;

public static class EnvironmentVariables
{

    public const string SlimWorkerDelayMilliseconds = "SLIM_WORKER_DELAY_MILLISECONDS";
    public const int SlimWorkerDelayMillisecondsDefault = 50;
    public const int SlimProxyMiddlewareTimeoutWaitWakeSyncFunctionMilliSecondsDefault = 10000;
    public const string TimeMaximumWaitForAtLeastOnePodStartedForSyncFunction = "TIME_MAXIMUM_WAIT_FOR_AT_LEAST_ONE_POD_STARTED_FOR_SYNC_FUNCTION";
    public const string ReplicasSynchronisationWorkerDelayMilliseconds = "REPLICAS_SYNCHRONISATION_WORKER_DELAY_MILLISECONDS";
    public const int ReplicasSynchronizationWorkerDelayMillisecondsDefault = 2000;

    public const string HistorySynchronisationWorkerDelayMilliseconds = "HISTORY_SYNCHRONISATION_WORKER_DELAY_MILLISECONDS";
    public const int HistorySynchronizationWorkerDelayMillisecondsDefault = 500;

    public const string ScaleReplicasWorkerDelayMilliseconds = "SCALE_REPLICAS_WORKER_DELAY_MILLISECONDS";

    public const int ScaleReplicasWorkerDelayMillisecondsDefault = 1000;

    public const string PodScaledUpByDefaultWhenInfrastructureHasNeverCalled = "POD_SCALED_UP_BY_DEFAULT_WHEN_INFRASTRUCTURE_HAS_NEVER_CALLED";
    public const bool PodScaledUpByDefaultWhenInfrastructureHasNeverCalledDefault = false;

    public const string BaseFunctionUrl = "BASE_FUNCTION_URL";
    public const string BaseFunctionUrlDefault = "http://{function_name}:8080/";

    public const string Namespace = "NAMESPACE";
    public const string NamespaceDefault = "default";

    public const string MockKubernetesFunctions = "MOCK_KUBERNETES_FUNCTIONS";
    public const string MockKubernetesFunctionsDefault = "";

    public const string SlimDataDirectory = "SLIMDATA_DIRECTORY";
    public const string SlimDataDirectoryDefault = "c://SlimData";
    public const string SlimDataPort = "SLIMDATA_PORT";
    public const int SlimDataPortDefault = 3262;

    public const string MockSlimData = "MOCK_SLIMDATA";

    public static int ReadInteger<T>(ILogger<T> logger, string environmentVariableName, int defaultInteger)
    {
        string valueString = Environment.GetEnvironmentVariable(environmentVariableName) ?? defaultInteger.ToString();
        if (int.TryParse(valueString, out int value))
        {
            return value;
        }
        logger.LogWarning("Cannot parse to int the environment variable {EnvironmentVariableName} with value {EnvironmentVariableValue}. Using default value {DefaultDelay}", environmentVariableName, valueString, defaultInteger);
        return defaultInteger;
    }

    public static bool ReadBoolean<T>(ILogger<T> logger, string environmentVariableName, bool defaultBoolean)
    {
        string valueString = Environment.GetEnvironmentVariable(environmentVariableName) ?? defaultBoolean.ToString();
        if (bool.TryParse(valueString, out bool value))
        {
            return value;
        }
        logger.LogWarning("Cannot parse to boolean the environment variable {EnvironmentVariableName} with value {EnvironmentVariableValue}. Using default value {DefaultDelay}", environmentVariableName, valueString, defaultBoolean);
        return defaultBoolean;
    }
}
