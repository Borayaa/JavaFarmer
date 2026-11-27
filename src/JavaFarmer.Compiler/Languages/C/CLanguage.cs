using JavaFarmer.Compiler.CFamily;

namespace JavaFarmer.Compiler
{
    /// <summary>C (C23, gcc -std=c23) as a JavaFarmer language. See docs/languages/c.md.</summary>
    public sealed class CLanguage : CFamilyLanguage
    {
        public const string Template = "#include \"farm.h\"\n\nint main(void) {\n\t\n}\n";
        public override string Id => "c";
        public override string DisplayName => "C";
        public override string FileExtension => ".c";
        internal override Dialect Dialect => Dialect.C;

        public override string NewFileTemplate(string fileName, out int caret)
        {
            caret = Template.IndexOf('\t') + 1;
            return Template;
        }
    }
}
