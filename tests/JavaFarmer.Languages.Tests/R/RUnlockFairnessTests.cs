using static JavaFarmer.Languages.Tests.R.RTestUtil;

namespace JavaFarmer.Languages.Tests.R;

/// <summary>SPEC §7: R scaffolding costs nothing; each construct costs its Python twin's unlock.</summary>
public class RUnlockFairnessTests
{
    [Fact]
    public void ImplicitReturn_IsSynthetic_ExplicitReturnIsUser()
    {
        var r = Compile("f <- function(x) {\n  x + 1\n}\ng <- function(x) {\n  return(x)\n}");
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, "return x + 1").Kind);
        Assert.Equal(SegmentKind.User, SegmentOf(r, "return x\n").Kind);
    }

    [Fact]
    public void SeqLen_StartIsSynthetic_InclusiveEndIsStructural()
    {
        var r = Compile("n <- 3\nfor (i in seq_len(n)) print(i)");
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, "1, n").Kind);
        Assert.Equal(SegmentKind.Structural, SegmentOf(r, " + 1").Kind);
        Assert.Equal(SegmentKind.User, SegmentOf(r, "range").Kind);
        var r2 = Compile("v <- c(1)\nfor (i in seq_along(v)) print(i)");
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r2, "len").Kind);
    }

    [Fact]
    public void OneBasedOffset_IsSynthetic()
    {
        var r = Compile("v <- c(1, 2)\ni <- 1\nprint(v[[i]])");
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, " - 1").Kind);
    }

    [Fact]
    public void Copies_AreSynthetic()
    {
        var r = Compile("a <- c(1)\nb <- a\nf <- function(p) {\n  p[[1]] <- 2\n}");
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, "list(").Kind);
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, "p = list(p)").Kind);
    }

    [Fact]
    public void PasteSeparators_AreSynthetic_TheJoinIsUser()
    {
        var r = Compile("n <- 1\nprint(paste(\"a\", n))");
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, " + \" \"").Kind);
        Assert.Equal(SegmentKind.User, SegmentOf(r, " + ", 1).Kind);
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, "str(").Kind);
    }

    [Fact]
    public void CatSepEmpty_JoinIsSynthetic_PrintIsUser()
    {
        var r = Compile("n <- 1\ncat(\"a\", n, sep = \"\")");
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, " + ").Kind);
        Assert.Equal(SegmentKind.User, SegmentOf(r, "print").Kind);
    }

    [Fact]
    public void LocalCopyOfAGlobal_AndSwitchDefault_AreSynthetic()
    {
        var r = Compile("g <- c(1)\nf <- function() {\n  g[[1]] <- 2\n}\ns <- \"x\"\ny <- switch(s, a = 1)");
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, "g_l = list(g)").Kind);
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, "y = None").Kind);
    }

    [Fact]
    public void Match_PlusOne_IsSynthetic()
    {
        var r = Compile("v <- c(3)\nprint(match(3, v))");
        Assert.Equal(SegmentKind.Synthetic, SegmentOf(r, " + 1").Kind);
    }

    [Fact]
    public void Constructs_CostTheirPythonTwin()
    {
        var r = Compile("repeat {\n  break\n}\nwhile (TRUE) break\nf <- function() 1\nx <- if (TRUE) 1 else 2");
        Assert.Equal(SegmentKind.User, SegmentOf(r, "while").Kind);
        Assert.Equal(SegmentKind.User, SegmentOf(r, "def").Kind);
        Assert.Equal(SegmentKind.User, SegmentOf(r, "if").Kind);
        Assert.Null(SegmentOf(r, "while").DependencyOverride);
    }
}
