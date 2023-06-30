namespace SlimFaas;

public static class EnvironmentVariables
{

    public static int ReadInteger<T>(ILogger<T> logger, string environmentVariableName, int defaultDelay)
    {
        string valueString = Environment.GetEnvironmentVariable(environmentVariableName) ?? defaultDelay.ToString();
        int value;
        if (int.TryParse(valueString, out value))
        {
            return value;
        }

        logger.LogWarning("Cannot parse to int the environment variable {EnvironmentVariableName} with value {EnvironmentVariableValue}. Using default value {DefaultDelay}", environmentVariableName, valueString, defaultDelay);
        return defaultDelay;
    }
}
