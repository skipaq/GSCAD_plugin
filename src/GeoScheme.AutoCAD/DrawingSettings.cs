using System;
using Autodesk.AutoCAD.DatabaseServices;

namespace GeoScheme.AutoCAD
{
    /// <summary>
    /// Настройки, которые живут в самом чертеже (словарь GEOSCHEME),
    /// чтобы при передаче файла коллеге ничего не терялось.
    /// </summary>
    public static class DrawingSettings
    {
        private const string DictName = "GEOSCHEME";
        private const string UnitsKey = "UNITS";   // "m" или "mm"

        public static string GetUnits(Database db)
        {
            var value = Read(db, UnitsKey);
            return string.IsNullOrEmpty(value) ? "m" : value;
        }

        public static void SetUnits(Database db, string units)
        {
            Write(db, UnitsKey, units);
        }

        /// <summary>Сколько миллиметров в одной единице чертежа.</summary>
        public static double MillimetersPerUnit(Database db)
        {
            return GetUnits(db) == "mm" ? 1.0 : 1000.0;
        }

        private static string Read(Database db, string key)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (!nod.Contains(DictName))
                    return null;

                var dict = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForRead);
                if (!dict.Contains(key))
                    return null;

                var xrec = (Xrecord)tr.GetObject(dict.GetAt(key), OpenMode.ForRead);
                var data = xrec.Data?.AsArray();
                if (data == null || data.Length == 0)
                    return null;

                return data[0].Value as string;
            }
        }

        private static void Write(Database db, string key, string value)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);

                DBDictionary dict;
                if (nod.Contains(DictName))
                    dict = (DBDictionary)tr.GetObject(nod.GetAt(DictName), OpenMode.ForWrite);
                else
                {
                    dict = new DBDictionary();
                    nod.SetAt(DictName, dict);
                    tr.AddNewlyCreatedDBObject(dict, true);
                }

                var xrec = new Xrecord();
                xrec.Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, value));

                if (dict.Contains(key))
                {
                    var old = (Xrecord)tr.GetObject(dict.GetAt(key), OpenMode.ForWrite);
                    old.Data = xrec.Data;
                }
                else
                {
                    dict.SetAt(key, xrec);
                    tr.AddNewlyCreatedDBObject(xrec, true);
                }

                tr.Commit();
            }
        }
    }
}
