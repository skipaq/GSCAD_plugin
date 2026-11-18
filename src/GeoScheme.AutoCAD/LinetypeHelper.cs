using Autodesk.AutoCAD.DatabaseServices;

namespace GeoScheme.AutoCAD
{
    /// <summary>Подбор типа линии с загрузкой из acadiso.lin, если его нет в чертеже.</summary>
    internal static class LinetypeHelper
    {
        public static string Resolve(Database db, params string[] preferred)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var table = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);

                foreach (var name in preferred)
                    if (table.Has(name))
                        return name;

                tr.Commit();
            }

            foreach (var name in preferred)
            {
                try
                {
                    db.LoadLineTypeFile(name, "acadiso.lin");
                }
                catch
                {
                    // файла может не быть — пробуем следующий
                }

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var table = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                    var has = table.Has(name);
                    tr.Commit();
                    if (has)
                        return name;
                }
            }

            return "Continuous";
        }
    }
}
