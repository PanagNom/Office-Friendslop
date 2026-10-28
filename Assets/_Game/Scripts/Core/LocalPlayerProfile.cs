using UnityEngine;

public static class LocalPlayerProfile
{
    private const string PlayerNameKey = "PlayerName";
    public const int MaxNameLength = 20;
    public static string PlayerName
    {
        get => PlayerPrefs.GetString(PlayerNameKey);
        set => PlayerPrefs.SetString(PlayerNameKey, CleanName(value));
    }

    public static string CleanName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }

        name = name.Trim();

        if (name.Length > MaxNameLength)
        {
            name = name.Substring(0, MaxNameLength);
        }

        return name;
    }
}
