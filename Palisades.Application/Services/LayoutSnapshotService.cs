using Palisades.Helpers;
using Palisades.Model;
using System;
using Palisades;
using Palisades.Serialization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Xml;
using System.Xml.Serialization;

namespace Palisades.Services
{
    public static class LayoutSnapshotService
    {
        private static readonly XmlSerializer SnapshotSerializer = new(typeof(LayoutSnapshot), new[] { typeof(SnapshotEntry) });

        // Désérialisation durcie (CA5369) : DTD interdit et aucun résolveur d'entités externes.
        // Les snapshots peuvent être importés depuis un dossier externe (ImportSnapshot).
        private static readonly XmlReaderSettings SafeXmlSettings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };

        private static LayoutSnapshot? DeserializeSnapshot(string path)
        {
            using var stream = File.OpenRead(path);
            using var xmlReader = XmlReader.Create(stream, SafeXmlSettings);
            return SnapshotSerializer.Deserialize(xmlReader) as LayoutSnapshot;
        }

        public static LayoutSnapshot SaveSnapshot(string name)
        {
            int screenCount = 1;
            try
            {
                screenCount = System.Windows.Forms.Screen.AllScreens.Length;
            }
            catch { }

            var snapshot = new LayoutSnapshot
            {
                Name = name,
                CreatedAt = DateTime.UtcNow,
                ScreenWidth = (int)SystemParameters.PrimaryScreenWidth,
                ScreenHeight = (int)SystemParameters.PrimaryScreenHeight,
                ScreenCount = screenCount
            };
            var savedDir = PDirectory.GetPalisadesDirectory();
            if (!Directory.Exists(savedDir))
                return snapshot;
            foreach (var dir in Directory.GetDirectories(savedDir))
            {
                var stateFile = Path.Combine(dir, "state.xml");
                if (!File.Exists(stateFile)) continue;
                var identifier = Path.GetFileName(dir);
                var content = File.ReadAllText(stateFile);
                string? groupId = null;
                int tabOrder = 0;
                try
                {
                    using var sr = new StringReader(content);
                    using var xr = XmlReader.Create(sr, SafeXmlSettings);
                    if (PalisadeXmlSerialization.PalisadeModelSerializer.Deserialize(xr) is PalisadeModelBase model)
                    {
                        groupId = model.GroupId;
                        tabOrder = model.TabOrder;
                    }
                }
                catch (Exception ex)
                {
                    PalisadeDiagnostics.Log("LayoutSnapshot", "Lecture state.xml pour métadonnées snapshot : " + identifier, ex);
                }

                snapshot.Entries.Add(new SnapshotEntry
                {
                    PalisadeIdentifier = identifier,
                    GroupId = groupId,
                    TabOrder = tabOrder,
                    StateXmlContent = content
                });
            }
            var snapDir = Path.Combine(PDirectory.GetSnapshotsDirectory(), snapshot.Id);
            PDirectory.EnsureExists(PDirectory.GetSnapshotsDirectory());
            Directory.CreateDirectory(snapDir);
            using (var writer = new StreamWriter(Path.Combine(snapDir, "snapshot.xml")))
                SnapshotSerializer.Serialize(writer, snapshot);
            return snapshot;
        }

        public static List<LayoutSnapshot> ListSnapshots()
        {
            var list = new List<LayoutSnapshot>();
            var snapDir = PDirectory.GetSnapshotsDirectory();
            if (!Directory.Exists(snapDir)) return list;
            foreach (var dir in Directory.GetDirectories(snapDir))
            {
                var path = Path.Combine(dir, "snapshot.xml");
                if (!File.Exists(path)) continue;
                try
                {
                    if (DeserializeSnapshot(path) is LayoutSnapshot s)
                    {
                        s.Id = Path.GetFileName(dir);
                        list.Add(s);
                    }
                }
                catch (Exception ex)
                {
                    PalisadeDiagnostics.Log("LayoutSnapshot", "ListSnapshots : " + path, ex);
                }
            }
            return list.OrderByDescending(s => s.CreatedAt).ToList();
        }

        public static void RestoreSnapshot(string snapshotId)
        {
            var path = Path.Combine(PDirectory.GetSnapshotsDirectory(), snapshotId, "snapshot.xml");
            if (!File.Exists(path)) return;
            LayoutSnapshot? snapshot = DeserializeSnapshot(path);
            if (snapshot?.Entries == null) return;

            if (snapshot.SchemaVersion == 0)
                snapshot.SchemaVersion = 1;

            PalisadesManager.CloseAllPalisades();
            var savedDir = PDirectory.GetPalisadesDirectory();
            if (Directory.Exists(savedDir))
            {
                foreach (var dir in Directory.GetDirectories(savedDir))
                    Directory.Delete(dir, true);
            }
            PDirectory.EnsureExists(savedDir);
            foreach (var entry in snapshot.Entries)
            {
                var palDir = Path.Combine(savedDir, entry.PalisadeIdentifier);
                Directory.CreateDirectory(palDir);
                File.WriteAllText(Path.Combine(palDir, "state.xml"), entry.StateXmlContent);
            }
            PalisadesManager.LoadPalisades();
            ApplyRescaleIfNeeded(snapshot);
        }

        /// <summary>Recalcule position/taille des palisades si la résolution a changé (10.3.3).</summary>
        public static void ApplyRescaleIfNeeded(LayoutSnapshot snapshot)
        {
            int currentW = (int)SystemParameters.PrimaryScreenWidth;
            int currentH = (int)SystemParameters.PrimaryScreenHeight;
            if (snapshot.ScreenWidth <= 0 || snapshot.ScreenHeight <= 0) return;
            if (snapshot.ScreenWidth == currentW && snapshot.ScreenHeight == currentH) return;
            PalisadesManager.ApplyRescale(snapshot.ScreenWidth, snapshot.ScreenHeight, currentW, currentH);
        }

        public static void DeleteSnapshot(string snapshotId)
        {
            var dir = Path.Combine(PDirectory.GetSnapshotsDirectory(), snapshotId);
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }

        public static void RenameSnapshot(string snapshotId, string newName)
        {
            var path = Path.Combine(PDirectory.GetSnapshotsDirectory(), snapshotId, "snapshot.xml");
            if (!File.Exists(path)) return;
            LayoutSnapshot? snapshot = DeserializeSnapshot(path);
            if (snapshot == null) return;
            snapshot.Name = newName ?? "";
            using (var writer = new StreamWriter(path))
                SnapshotSerializer.Serialize(writer, snapshot);
        }

        public static bool ExportSnapshot(string snapshotId, string destinationFolder)
        {
            var src = Path.Combine(PDirectory.GetSnapshotsDirectory(), snapshotId);
            if (!Directory.Exists(src)) return false;
            var dest = Path.Combine(destinationFolder, Path.GetFileName(src));
            if (Directory.Exists(dest)) return false;
            PDirectory.CopyDirectory(src, dest);
            return true;
        }

        public static string? ImportSnapshot(string sourceFolder)
        {
            var snapshotPath = Path.Combine(sourceFolder, "snapshot.xml");
            if (!File.Exists(snapshotPath)) return null;
            LayoutSnapshot? snapshot = DeserializeSnapshot(snapshotPath);
            if (snapshot?.Id == null) return null;
            var newId = Guid.NewGuid().ToString();
            var destDir = Path.Combine(PDirectory.GetSnapshotsDirectory(), newId);
            PDirectory.EnsureExists(PDirectory.GetSnapshotsDirectory());
            PDirectory.CopyDirectory(sourceFolder, destDir);
            var destXml = Path.Combine(destDir, "snapshot.xml");
            if (File.Exists(destXml))
            {
                snapshot.Id = newId;
                snapshot.CreatedAt = DateTime.UtcNow;
                using (var writer = new StreamWriter(destXml))
                    SnapshotSerializer.Serialize(writer, snapshot);
            }
            return newId;
        }


        /// <summary>10.3.7 — Auto-save au exit : enregistre un snapshot "Auto-save - {date}", garde les 3 derniers.</summary>
        public static void SaveAutoSnapshotAndPrune(int keepCount = 3)
        {
            try
            {
                var name = "Auto-save - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                SaveSnapshot(name);
                var all = ListSnapshots();
                var autoSaves = all.Where(s => s.Name.StartsWith("Auto-save - ", StringComparison.Ordinal))
                    .OrderByDescending(s => s.CreatedAt)
                    .ToList();
                foreach (var s in autoSaves.Skip(keepCount))
                    DeleteSnapshot(s.Id);
            }
            catch (Exception ex)
            {
                PalisadeDiagnostics.Log("LayoutSnapshot", "SaveAutoSnapshotAndPrune", ex);
            }
        }
    }
}
