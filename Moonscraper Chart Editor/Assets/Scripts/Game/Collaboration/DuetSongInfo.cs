// See LICENSE in project root for license information.

using System.Collections.Generic;
using System.Globalization;
using MoonscraperChartEditor.Song;

/// <summary>
/// The song's details from the Song Properties window (name, artist, charter and so on) as text, field by field,
/// so a change to one field can be sent and applied without touching the others.
/// Audio files aren't included: only the host chooses those.
/// </summary>
public static class DuetSongInfo
{
    public static Dictionary<string, string> Read(Song song)
    {
        Metadata metaData = song.metaData;
        return new Dictionary<string, string>
        {
            { "name", song.name ?? string.Empty },
            { "artist", metaData.artist ?? string.Empty },
            { "charter", metaData.charter ?? string.Empty },
            { "album", metaData.album ?? string.Empty },
            { "year", metaData.year ?? string.Empty },
            { "genre", metaData.genre ?? string.Empty },
            { "mediaType", metaData.mediatype ?? string.Empty },
            { "difficulty", metaData.difficulty.ToString(CultureInfo.InvariantCulture) },
            { "offset", song.offset.ToString("R", CultureInfo.InvariantCulture) },
            { "ini", SongIniFunctions.IniTextFromSongProperties(song.iniProperties) },
        };
    }

    // Applies only the fields present in info
    public static void Apply(Song song, Dictionary<string, string> info)
    {
        Metadata metaData = song.metaData;
        string value;

        if (info.TryGetValue("name", out value)) song.name = value;
        if (info.TryGetValue("artist", out value)) metaData.artist = value;
        if (info.TryGetValue("charter", out value)) metaData.charter = value;
        if (info.TryGetValue("album", out value)) metaData.album = value;
        if (info.TryGetValue("year", out value)) metaData.year = value;
        if (info.TryGetValue("genre", out value)) metaData.genre = value;
        if (info.TryGetValue("mediaType", out value)) metaData.mediatype = value;

        int difficulty;
        if (info.TryGetValue("difficulty", out value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out difficulty))
            metaData.difficulty = difficulty;

        float offset;
        if (info.TryGetValue("offset", out value) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out offset))
            song.offset = offset;

        if (info.TryGetValue("ini", out value))
            song.iniProperties = SongIniFunctions.SongIniFromString(value);
    }
}
