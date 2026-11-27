namespace JavaFarmer.Languages.Tests;

/// <summary>C++: g++ -std=c++23 against the generated farm.hpp; differential runs build with g++ and run. See docs/languages/cpp.md.</summary>
public sealed class CppHooks : LanguageHooks
{
    public override string Id => "cpp";

    public const string Flags = "-std=c++23 -Wall -Werror -Wno-unused -Wno-sign-compare";

    static IReadOnlyDictionary<string, string> Stub(ToolContext ctx) =>
        new Dictionary<string, string> { ["farm.hpp"] = ((CFamilyLanguage)Compiler.Languages.Get("cpp")).ApiStubFor(ctx.Sources.Values) };

    public override Toolchain Conformance => new Toolchain
    {
        Exe = "g++",
        SkipHint = "install g++ 14+ (C++23)",
        ExtraFiles = Stub,
        Args = ctx => Flags + " -fsyntax-only " + string.Join(" ", ctx.Sources.Keys.Select(ctx.Quote)),
    };

    public override Toolchain Runner => new Toolchain
    {
        Exe = "g++",
        SkipHint = "install g++ 14+ (C++23)",
        ExtraFiles = Stub,
        Args = ctx => Flags + " -o prog " + string.Join(" ", ctx.Sources.Keys.Select(ctx.Quote)),
        Then = new Toolchain { Exe = "./prog", Args = _ => "" },
    };

    public override string PythonPrelude => CFamilyPrelude.Cpp;
}
