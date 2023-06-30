namespace SlimFaas;

public static class EnvironmentVariables
{

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
