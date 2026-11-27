using System.Text.RegularExpressions;
using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin.Tests;

public class HighlighterTests
{
    /// <summary>Tiny Java lexer so colouring is tested independently of the compiler.</summary>
    private static IReadOnlyList<(int Start, int Length, TokenKind Kind)> FakeLex(string s)
    {
        var keywords = new HashSet<string> { "void", "int", "while", "if", "else", "return", "new", "for" };
        var types = new HashSet<string> { "String", "List", "ArrayList", "Integer" };
        var consts = new HashSet<string> { "true", "false", "null" };
        var list = new List<(int, int, TokenKind)>();
        foreach (Match m in Regex.Matches(s, @"//[^\n]*|""(?:[^""\\]|\\.)*""|\d+(?:\.\d+)?|[A-Za-z_]\w*|\s+|."))
        {
            var t = m.Value;
            TokenKind k;
            if (t.StartsWith("//")) k = TokenKind.Comment;
            else if (t.StartsWith("\"")) k = TokenKind.String;
            else if (char.IsDigit(t[0])) k = TokenKind.Number;
            else if (char.IsWhiteSpace(t[0])) k = TokenKind.Whitespace;
            else if (char.IsLetter(t[0]) || t[0] == '_')
                k = keywords.Contains(t) ? TokenKind.Keyword : types.Contains(t) ? TokenKind.Type : consts.Contains(t) ? TokenKind.Constant : TokenKind.Identifier;
            else k = TokenKind.Punctuation;
            list.Add((m.Index, m.Length, k));
        }
        return list;
    }

    private static string Strip(string markup) =>
        Regex.Replace(markup.Replace("<noparse><</noparse>", "<"), "</?(color|mark)(=[^>]*)?>", "");

    private static string C(string code, string search = "", int idx = -1) => JavaHighlighter.Color(code, search, idx, FakeLex);

    [Fact]
    public void UsesTheGamePalette()
    {
        string code = "void main() {\n    int x = 5; // hi\n    move(North);\n    String s = \"a\";\n    x.size();\n}";
        string o = C(code);
        Assert.Contains("<color=#9e7124>void</color>", o);
        Assert.Contains("<color=#e6c87c>main</color>", o);
        Assert.Contains("<color=#9e7124>int</color>", o);
        Assert.Contains("<color=#7f8a36>5</color>", o);
        Assert.Contains("<color=#666666>// hi</color>", o);
        Assert.Contains("<color=#e6c87c>move</color>", o);
        Assert.Contains("<color=#7f8a36>North</color>", o);
        Assert.Contains("<color=#7f8a36>String</color>", o);
        Assert.Contains("<color=#8f5d28>\"a\"</color>", o);
        Assert.Contains("<color=#e6c87c>size</color>", o);
        Assert.Equal(code, Strip(o));
    }

    [Fact]
    public void FieldAfterDotIsBuiltinColour()
    {
        Assert.Contains("<color=#7f8a36>Carrot</color>", C("plant(Entities.Carrot);"));
    }

    [Fact]
    public void GenericsAreEscapedSoTheOverlayKeepsTheSameCharacters()
    {
        string code = "List<Integer> l = new ArrayList<>(); if (a<b) {}";
        string o = C(code);
        Assert.Contains("<noparse><</noparse>", o);
        Assert.DoesNotContain("<Integer>", o.Replace("<noparse><</noparse>", "&"));
        Assert.Equal(code, Strip(o));
    }

    [Fact]
    public void SearchMarks_InsideTokenKeepColour_AcrossTokensUseOuterMark()
    {
        string code = "harvest(); harvest();";
        string o = C(code, "vest", 3);
        Assert.Contains("<color=#e6c87c>har<mark=#ffffff14>vest</mark></color>", o);
        Assert.Contains("<color=#e6c87c>har<mark=#ffffff09>vest</mark></color>", o);
        Assert.Equal(code, Strip(o));

        string o2 = C(code, "t(", -1);
        Assert.Contains("<mark=#ffffff05>t(</mark>", o2);
        Assert.Equal(code, Strip(o2));

        string o3 = C("x = 1; X = 2;", "x", 0);
        Assert.Equal("x = 1; X = 2;", Strip(o3));
        Assert.Contains("<mark=#ffffff10>x</mark>", o3);
        Assert.Contains("<mark=#ffffff05>X</mark>", o3);
    }

    [Fact]
    public void NeverThrows_OnOddInput_AndCoversTextWithoutTokens()
    {
        Assert.Equal("", JavaHighlighter.Color("", "x", 0, FakeLex));
        Assert.Equal("abc", JavaHighlighter.Color("abc", "", -1, _ => null));
        Assert.Equal("abc", JavaHighlighter.Color("abc", "", -1, _ => throw new Exception("lexer bug")));
        // overlapping / out-of-range tokens are tolerated
        var weird = JavaHighlighter.Color("abc", "", -1, _ => new List<(int, int, TokenKind)> { (-3, 2, TokenKind.Keyword), (1, 99, TokenKind.Number), (5, 1, TokenKind.String) });
        Assert.Equal("abc", Strip(weird));
    }

    [SkippableFact]
    public void RealJavaSyntaxHighlighter_RoundTrips()
    {
        string code = "void main() {\n    while (true) { harvest(); } // loop\n    List<Integer> xs = new ArrayList<>();\n}";
        Skip.If(JavaSyntax.Highlight(code).Count == 0, "JavaSyntax.Highlight is still the stub");
        string o = JavaHighlighter.Color(code, "", -1);
        Assert.Equal(code, Strip(o));
        Assert.Contains("<color=#9e7124>while</color>", o);
        Assert.Contains("<color=#e6c87c>harvest</color>", o);
        Assert.Contains("<color=#666666>// loop</color>", o);
    }
}
