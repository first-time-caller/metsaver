namespace MetSaver;

/// <summary>What the loader is doing right now, for the "finding a painting" overlay.</summary>
/// <param name="Message">Headline, e.g. "Downloading 'The Feast of Achelous'".</param>
/// <param name="Fraction">0..1 when progress is measurable (a download); null = indeterminate.</param>
/// <param name="Eta">Estimated time remaining, when it can be estimated.</param>
/// <param name="Detail">Smaller line under the headline.</param>
public sealed record LoadStatus(string Message, double? Fraction = null, TimeSpan? Eta = null, string? Detail = null);
