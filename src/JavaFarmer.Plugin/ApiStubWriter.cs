using System.Collections.Generic;
using System.Text;
using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin
{
    /// <summary>Builds JavaFarmerApi.java: a reference of the game API for external editors (not compiled by the game).</summary>
    public static class ApiStubWriter
    {
        public const string FileName = "JavaFarmerApi.java";

        public static string Build(IEnumerable<ApiFunction> functions)
        {
            var sb = new StringBuilder();
            sb.Append("// JavaFarmer game API reference, written by the JavaFarmer mod.\n");
            sb.Append("// The game never runs this file. It lists the functions your Java code can call.\n");
            sb.Append("// All functions are available without a prefix, as if statically imported.\n");
            sb.Append("// Game constants keep the game's names: North, East, South, West, Entities.Carrot, Items.Hay, ...\n");
            sb.Append("// \"A|B x\" means the parameter accepts an A or a B.\n\n");
            sb.Append("interface JavaFarmerApi {\n");
            if (functions != null)
            {
                foreach (var f in functions)
                {
                    if (f == null) continue;
                    string sig = string.IsNullOrEmpty(f.Signature) ? (f.ReturnType ?? "Object") + " " + f.JavaName + "()" : f.Signature;
                    bool qualified = f.JavaName != null && f.JavaName.IndexOf('.') >= 0;
                    sb.Append(qualified ? "    // " : "    ").Append(sig.TrimEnd(';')).Append(";");
                    if (!string.IsNullOrEmpty(f.PythonName)) sb.Append("   // game: ").Append(f.PythonName);
                    sb.Append('\n');
                }
            }
            sb.Append("}\n");
            return sb.ToString();
        }
    }
}
