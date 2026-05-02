namespace Material.Icons.UNO
{
    internal static class MaterialIconsUtils {
        public static void InitializeGeometryParser() {
            MaterialIconDataProvider.DisableCache();
            MaterialIconDataProvider.InitializeGeometryParser(MaterialIconParser.Parse);
        }
    }
}
