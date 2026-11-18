using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace GeoScheme.AutoCAD
{
    /// <summary>Библиотека DXF-блоков (легенды, условные обозначения) в профиле пользователя.</summary>
    public static class BlockLibrary
    {
        public static string Folder
        {
            get
            {
                var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(root, "GeoScheme", "Blocks");
            }
        }

        public static List<FrameLibrary.FrameInfo> Load()
        {
            var result = new List<FrameLibrary.FrameInfo>();

            try
            {
                if (!Directory.Exists(Folder))
                    return result;

                foreach (var path in Directory.GetFiles(Folder, "*.dxf"))
                {
                    var info = FrameLibrary.Read(path);
                    if (info != null)
                        result.Add(info);
                }
            }
            catch
            {
                // папка недоступна
            }

            return result;
        }

        /// <summary>Сохранить выделенные объекты как блок в библиотеку.</summary>
        public static bool SaveFromSelection(Editor ed, Database db, string name, out string error)
        {
            error = null;

            var selection = ed.GetSelection(new PromptSelectionOptions
            {
                MessageForAdding = "\nВыберите объекты для блока: "
            });

            if (selection.Status != PromptStatus.OK)
            {
                error = "Отменено.";
                return false;
            }

            try
            {
                Directory.CreateDirectory(Folder);

                var path = Path.Combine(Folder, Sanitize(name) + ".dxf");

                using (var target = new Database(true, false))
                {
                    var ids = new ObjectIdCollection();

                    foreach (var id in selection.Value.GetObjectIds())
                        ids.Add(id);

                    using (var targetTransaction = target.TransactionManager.StartTransaction())
                    {
                        var model = (BlockTableRecord)targetTransaction.GetObject(
                            target.CurrentSpaceId, OpenMode.ForWrite);

                        var mapping = new IdMapping();
                        db.WblockCloneObjects(ids, model.ObjectId, mapping,
                            DuplicateRecordCloning.Replace, false);

                        targetTransaction.Commit();
                    }

                    target.DxfOut(path, 16, false);
                }

                return true;
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool Rename(FrameLibrary.FrameInfo block, string newName, out string error)
        {
            error = null;

            try
            {
                var target = Path.Combine(Folder, Sanitize(newName) + ".dxf");
                File.Move(block.Path, target);
                return true;
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool Copy(FrameLibrary.FrameInfo block, string newName, out string error)
        {
            error = null;

            try
            {
                var target = Path.Combine(Folder, Sanitize(newName) + ".dxf");
                File.Copy(block.Path, target, true);
                return true;
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool Delete(FrameLibrary.FrameInfo block, out string error)
        {
            error = null;

            try
            {
                File.Delete(block.Path);
                return true;
            }
            catch (System.Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string Sanitize(string name)
        {
            foreach (var ch in Path.GetInvalidFileNameChars())
                name = name.Replace(ch, '_');

            return name.Trim();
        }
    }
}
