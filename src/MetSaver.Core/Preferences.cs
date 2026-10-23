using System.Text.Json;

namespace MetSaver;

/// <summary>Paintings you've marked as favourites (gallery mode, press F). They're shown more often.</summary>
public sealed class Preferences
{
    public const double FavouriteBoost = 3.0;
    readonly object _gate = new();
    readonly HashSet<int> _favourites;
    readonly string _path;

    public Preferences(string? path = null)
    {
        _path = path ?? Path.Combine(Paths.Home, "favourites.json");
        try { _favourites = JsonSerializer.Deserialize<HashSet<int>>(File.ReadAllText(_path)) ?? new(); }
        catch { _favourites = new(); }
    }

    public bool IsFavourite(int id) { lock (_gate) return _favourites.Contains(id); }
    public int Count { get { lock (_gate) return _favourites.Count; } }

    /// <summary>Flip the favourite flag; returns the new state.</summary>
    public bool Toggle(int id)
    {
        lock (_gate)
        {
            bool now = _favourites.Add(id);
            if (!now) _favourites.Remove(id);
            try { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); File.WriteAllText(_path, JsonSerializer.Serialize(_favourites)); }
            catch (Exception e) { Log.Error(e, "saving favourites"); }
            return now;
        }
    }

    public double Weight(int id) => IsFavourite(id) ? FavouriteBoost : 1.0;
}
