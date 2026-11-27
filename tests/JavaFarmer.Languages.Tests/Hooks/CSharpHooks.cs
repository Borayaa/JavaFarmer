namespace JavaFarmer.Languages.Tests;

/// <summary>
/// C# 12. Real-compiler conformance and differential execution run in-process with Roslyn
/// (tests/JavaFarmer.Languages.Tests/CSharp/CSharpRoslynTests.cs: CSharpRoslyn.Compile / Run against the generated
/// Farm.cs stub), so the harness' external-tool steps (c) and (d) are skipped here with that hint.
/// </summary>
public sealed class CSharpHooks : LanguageHooks
{
    public override string Id => "csharp";

    // print: True / False, null prints as an empty line, numbers like double.ToString()
    public override string PythonPrelude => Prelude.WithLiterals("", "True", "False");
}
