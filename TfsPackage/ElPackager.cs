using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.VersionControl.Client;
using log4net;

namespace TfsPackage
{
    public class ElPackager
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof (ElPackager));

        public const string TfsUrl = "http://dim10:8080/tfs/DefaultCollection";

        private readonly string _root;
        private readonly IEnumerable<int> _changesets;
        private readonly VersionControlServer _vc;
        private readonly Workspace _workspace;

        private readonly IList<string> _addedItems  = new List<string>();
        private readonly IList<string> _backedupItems = new List<string>();

        public IEnumerable<string> ExcludedItems { get; set; }

        private bool IsItemExcluded(Item item)
        {
            return ExcludedItems != null &&
                   ExcludedItems.Any(s => item.ServerItem.Contains(s));
        }

        private string ChangesetString
        {
            get
            {
                string res = _changesets.First().ToString();
                res = _changesets.Skip(1).Aggregate(res, (c, changeset) => c + ("_" + changeset));
                return res;
            }
        }

        public ElPackager(string root, IEnumerable<int> changesets)
        {
            _root = root;
            _changesets = changesets.OrderByDescending(i => i);
            var tpc = new TfsTeamProjectCollection(new Uri(TfsUrl));
            _vc = tpc.GetService<VersionControlServer>();
            _workspace = _vc.GetWorkspace(root);
        }

        bool VerifyBackup(Item item, Change change)
        {
            return item.ItemType == ItemType.File &&
                   !IsItemExcluded(item) &&
                   !_backedupItems.Contains(item.ServerItem) &&
                   !change.ChangeType.HasFlag(ChangeType.Add);
        }

        // based on: https://github.com/icsharpcode/SharpZipLib/wiki/Zip-Samples

        private void BuildBackupZip(ZipOutputStream zip, Changeset cs, string backupDir)
        {
            Console.WriteLine("Processing changeset " + cs.ChangesetId);
            foreach (var change in cs.Changes)
            {
                Item item = change.Item;
                if (!VerifyBackup(item, change))
                    continue;

                var localPath = _workspace.GetLocalItemForServerItem(item.ServerItem);

                string entryName = null;
                if (localPath.Contains(_root))
                    entryName = localPath.Substring(_root.Length).TrimStart('\\');

                // null if there is no mapping for the server file in this local path specified
                if (entryName == null)
                    continue;

                var filename = backupDir + "\\" + entryName;

                if (!File.Exists(filename))
                    // we may arrive her if the ChangeType == Merge and its actually an Add operation. 
                    // damn you TFS.
                    continue;

                Console.WriteLine("Packing " + filename);

                var entry = new ZipEntry(entryName);
                zip.PutNextEntry(entry);
                var buffer = new byte[4096];
                using (var sr = File.OpenRead(filename))
                    StreamUtils.Copy(sr, zip, buffer);

                _backedupItems.Add(item.ServerItem);

                zip.CloseEntry();
            }
        }

        public void ZipBackup(string backupDir)
        {
            var archiveName = ChangesetString + "_rollback.zip";
            var fsOut = File.Create(archiveName);
            Console.WriteLine("Creating backup archive " + archiveName + "...");
            var zip = new ZipOutputStream(fsOut);
            zip.SetLevel(8);

            foreach (var changeset in _changesets)
            {
                BuildBackupZip(zip, _vc.GetChangeset(changeset), backupDir);
            }

            zip.IsStreamOwner = true;
            zip.Close();
            Console.WriteLine();
        }

        bool VerifyChange(Item item, Change change)
        {
            return
                item.ItemType == ItemType.File &&
                !IsItemExcluded(item) &&
                !_addedItems.Contains(item.ServerItem) &&
                !change.ChangeType.HasFlag(ChangeType.Delete);
        }

        void BuildChangesetZip(ZipOutputStream zip, Changeset changeset)
        {
            Console.WriteLine("Processing changeset " + changeset.ChangesetId);
            foreach (var change in changeset.Changes)
            {
                var item = change.Item;
                Log.DebugFormat("Processing change for {0}", item.ServerItem);
                if (!VerifyChange(item, change))
                {
                    Log.DebugFormat("VerifyChange returned false, skipping {0}", item.ServerItem);
                    continue;
                }

                var localPath = _workspace.GetLocalItemForServerItem(item.ServerItem);

                string entryName = null;
                if (localPath.Contains(_root))
                    entryName = localPath.Substring(_root.Length).TrimStart('\\');
                if (entryName == null || item.ItemType != ItemType.File)
                    continue;

                Console.WriteLine("Packing " + entryName);

                var fileStream = item.DownloadFile();
                var entry = new ZipEntry(entryName);
                zip.PutNextEntry(entry);
                var buffer = new byte[4096];
                StreamUtils.Copy(fileStream, zip, buffer);

                _addedItems.Add(item.ServerItem);

                zip.CloseEntry();
            }
        }

        public void ZipChangesets()
        {
            var archiveName = ChangesetString + ".zip";
            var fsOut = File.Create(archiveName);
            Console.WriteLine("Creating deploy archive " + archiveName + "...");
            var zip = new ZipOutputStream(fsOut);
            zip.SetLevel(8);

            foreach (var changeset in _changesets)
            {
                BuildChangesetZip(zip, _vc.GetChangeset(changeset));
            }

            zip.IsStreamOwner = true;
            zip.Close();
        }

        public bool VerifyBackup(string backupDir)
        {
            Console.WriteLine(Environment.NewLine + "Verifying backup integrity...");
            var fs = File.OpenRead(ChangesetString + "_rollback.zip");
            var zf = new ZipFile(fs);
            foreach (ZipEntry zipEntry in zf)
            {
                if (!zipEntry.IsFile)
                    continue;
                var filename = backupDir + "\\" + zipEntry.Name;
                Console.WriteLine("Checking {0} == {1}", zipEntry.Name, filename);
                var zipStream = zf.GetInputStream(zipEntry);
                var md5 = MD5.Create();
                var zipFileHash = md5.ComputeHash(zipStream);
                using (var sr = File.OpenRead(filename))
                {
                    var backupHash = md5.ComputeHash(sr);
                    if (!CompareHashes(zipFileHash, backupHash))
                        return false;
                }
            }
            return true;
        }

        private bool CompareHashes(byte[] hashOne, byte[] hashTwo)
        {
            int i = 0;
            while ((i < hashOne.Length) && (hashOne[i] == hashTwo[i]))
                i++;
            return i == hashOne.Length;
        }
    }
}