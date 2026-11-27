using JavaFarmer.Compiler.CFamily;

namespace JavaFarmer.Compiler
{
    /// <summary>C++ (C++23, g++ -std=c++23) as a JavaFarmer language. See docs/languages/cpp.md.</summary>
    public sealed class CppLanguage : CFamilyLanguage
    {
        public const string Template = "#include \"farm.hpp\"\n\nint main() {\n\t\n}\n";
        public override string Id => "cpp";
        public override string DisplayName => "C++";
        public override string FileExtension => ".cpp";
        internal override Dialect Dialect => Dialect.Cpp;

        public override string NewFileTemplate(string fileName, out int caret)
        {
            caret = Template.IndexOf('\t') + 1;
            return Template;
        }
    }
}
