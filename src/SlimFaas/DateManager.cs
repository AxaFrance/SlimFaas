using NodaTime.TimeZones;
using NodaTime.TimeZones.Cldr;

public class DateManager()
{
    private static TimeZoneInfo GetTimeZoneInfo(IEnumerable<MapZone> territoryLocations)
    {
        TimeZoneInfo result = territoryLocations
            .Select(l => l.WindowsId)
            .Select(TimeZoneInfo.FindSystemTimeZoneById)
		    .Aggregate((tz1, tz2) => tz1.BaseUtcOffset < tz2.BaseUtcOffset ? tz1 : tz2); //pick timezone with the minimum offset
		return result;
    }

    private static Dictionary<string, TimeZoneInfo> GetTimeZoneInfosByCode()
    {
        TzdbDateTimeZoneSource source = TzdbDateTimeZoneSource.Default;
		return source.WindowsMapping.MapZones.GroupBy(z => z.Territory).ToDictionary(grp => grp.Key, GetTimeZoneInfo);
    }

    public static TimeZoneInfo GetTimeZoneInfoFromCountryCode(string isoCountryCode)
    {
        Dictionary<string, TimeZoneInfo> timeZonesDict = GetTimeZoneInfosByCode();
		foreach (KeyValuePair<string, TimeZoneInfo> mappedZone in timeZonesDict)
		{
			if (mappedZone.Key == isoCountryCode)
			{
				return mappedZone.Value;
			}
		}

		Console.WriteLine("ERROR - No TimeZoneInfoFound for culture code. Falling back to UTC.");
		return timeZonesDict["GB"];
    }
}