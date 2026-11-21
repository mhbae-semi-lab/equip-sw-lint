using System.IO;
using System.Xml.Linq;

namespace Sample.Equipment.Bad
{
    public sealed class Recipe
    {
        public string Name { get; set; } = "";
        public double Exposure { get; set; }
        public override string ToString() => Name + ";" + Exposure;
    }

    // Saves inspection recipes and machine configuration next to the program.
    public sealed class RecipeStore
    {
        private readonly string _folder;

        public RecipeStore(string folder) { _folder = folder; }

        public void Save(Recipe recipe)
        {
            var path = Path.Combine(_folder, recipe.Name + ".rcp");
            File.WriteAllText(path, recipe.ToString()); // expect: ESL006
        }

        public void SaveConfig(XDocument doc, string configPath) => doc.Save(configPath); // expect: ESL006
    }
}
