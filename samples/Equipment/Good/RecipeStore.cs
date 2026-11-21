using System.IO;
using System.Xml.Linq;

namespace Sample.Equipment.Good
{
    public sealed class Recipe
    {
        public string Name { get; set; } = "";
        public double Exposure { get; set; }
        public override string ToString() => Name + ";" + Exposure;
    }

    // Same store, fixed: write a temp file in the same folder, then swap it in.
    public sealed class RecipeStore
    {
        private readonly string _folder;

        public RecipeStore(string folder) { _folder = folder; }

        public void Save(Recipe recipe)
        {
            var path = Path.Combine(_folder, recipe.Name + ".rcp");
            var tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var w = new StreamWriter(fs))
            {
                w.Write(recipe.ToString());
                w.Flush();
                fs.Flush(true);
            }
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
            else File.Move(tmp, path);
        }

        public void SaveConfig(XDocument doc, string configPath)
        {
            var tmp = configPath + ".tmp";
            doc.Save(tmp);
            File.Move(tmp, configPath, overwrite: true);
        }
    }
}
