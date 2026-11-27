namespace JavaFarmer.Languages.Tests;

/// <summary>
/// GameApi is process-wide state. Tests that call GameApi.Register / Unregister (extension builtins) belong to this
/// collection (or to another collection with DisableParallelization, such as "perf" and "csharp-roslyn"): xUnit runs
/// those collections one at a time after every parallel collection has finished, so a test that reads
/// GameApi.Builtins (API stubs, completion lists, docs) never sees a test builtin come or go half-way.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GameApiRegistrations
{
    public const string Name = "GameApi registrations";
}
