using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;

namespace GeoScheme.AutoCAD
{
    /// <summary>
    /// Слои плагина: у каждого типа работы свой слой с префиксом GS_.
    /// </summary>
    public static class Layers
    {
        public const string Underlay = "GS_подложка";
        public const string Axis = "GS_оси";
        public const string Objects = "GS_объекты";
        public const string Dimensions = "GS_размеры";
        public const string DimensionsFact = "GS_размеры_факт";
        public const string Deviations = "GS_отклонения";

        public const short UnderlayColor = 8;   // серый
        public const short AxisColor = 72;
        public const short ObjectsColor = 5;    // синий
        public const short DimensionsColor = 2; // жёлтый
        public const short DimensionsFactColor = 4; // голубой
        public const short DeviationsColor = 6; // маджента

        public static ObjectId Ensure(Database db, Transaction tr, string name, short colorIndex, bool plottable)
        {
            var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (table.Has(name))
                return table[name];

            table.UpgradeOpen();
            var layer = new LayerTableRecord
            {
                Name = name,
                Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex),
                IsPlottable = plottable
            };

            table.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
            return layer.ObjectId;
        }
    }
}
