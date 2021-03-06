﻿using System;
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
        private readonly TfsChangesets _changesets;
        private readonly Workspace _workspace;

        private readonly IList<string> _addedItems  = new List<string>();
        private readonly IList<string> _backedupItems = new List<string>();

        public IEnumerable<string> ExcludedItems { get; set; }

        private bool IsItemExcluded(Item item)
        {
            return ExcludedItems != null &&
                   ExcludedItems.Any(s => item.ServerItem.Contains(s));
        }

        public ElPackager(string root, TfsChangesets changesets)
        {
            _root = root;
            var tpc = new TfsTeamProjectCollection(new Uri(TfsUrl));
            var vc = tpc.GetService<VersionControlServer>();
            _workspace = vc.GetWorkspace(root);
            var serverFolder = _workspace.GetServerItemForLocalItem(root);
            changesets.LoadChangesets(vc, serverFolder);
            _changesets = changesets;
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

                // support for .refresh files, grab the actual dll
                if (entryName.EndsWith(".refresh"))
                    entryName = entryName.Substring(0, entryName.Length - ".refresh".Length);

                var filename = backupDir + "\\" + entryName;

                if (!File.Exists(filename))
                    // we may arrive here if the ChangeType == Merge and its actually an Add operation. 
                    // damn you TFS.
                    continue;

                Console.WriteLine("Packing " + filename);

                var entry = new ZipEntry(entryName);
                zip.PutNextEntry(entry);
                var buffer = new byte[4096];
                using (FileStream sr = File.OpenRead(filename))
                    StreamUtils.Copy(sr, zip, buffer);

                _backedupItems.Add(item.ServerItem);

                zip.CloseEntry();
            }
        }

        public void ZipBackup(string backupDir)
        {
            var archiveName = _changesets + "_rollback.zip";
            var fsOut = File.Create(archiveName);
            Console.WriteLine("Creating backup archive " + archiveName + "...");
            var zip = new ZipOutputStream(fsOut);
            zip.SetLevel(8);

            foreach (var changeset in _changesets)
            {
                BuildBackupZip(zip, changeset, backupDir);
            }

            zip.IsStreamOwner = true;
            zip.Close();
            Console.WriteLine();
        }

        public void BuildDeleteScript(string deployDir)
        {
            var toDelete = _changesets
                .SelectMany(changeset => changeset.Changes,
                            (changeset, change) => _workspace.GetLocalItemForServerItem(change.Item.ServerItem))
                .Where(localPath => !File.Exists(localPath) && localPath.Contains(_root))
                .Select(localPath => deployDir.TrimEnd('\\') + "\\" + localPath.Substring(_root.Length).TrimStart('\\'))
                .ToList();

            if (toDelete.Any())
                File.WriteAllLines(string.Format("{0}_delete.bat", _changesets),
                                   toDelete.Select(p => string.Format("del {0}", p)));
        }

        bool VerifyChange(Item item, Change change)
        {
            return
                item.ItemType == ItemType.File &&
                !IsItemExcluded(item) &&
                !_addedItems.Contains(item.ServerItem) &&
                !change.ChangeType.HasFlag(ChangeType.Delete);
        }

        private Stream GetStreamFromItem(Item item)
        {
            var stream = item.DownloadFile();
            if (!item.ServerItem.EndsWith(".refresh"))
                return stream;
            string path = new StreamReader(stream).ReadToEnd().TrimEnd('\r', '\n');
            return new FileStream(path, FileMode.Open);
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
                {
                    Log.DebugFormat("Skipping {0}", item.ServerItem);
                    continue;
                }
                if (entryName.EndsWith(".refresh"))
                    entryName = entryName.Substring(0, entryName.Length - ".refresh".Length);

                Console.WriteLine("Packing " + entryName);

                Stream fileStream = GetStreamFromItem(item);
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
            var archiveName = _changesets + ".zip";
            var fsOut = File.Create(archiveName);
            Console.WriteLine("Creating deploy archive " + archiveName + "...");
            var zip = new ZipOutputStream(fsOut);
            zip.SetLevel(8);

            foreach (var changeset in _changesets)
            {
                BuildChangesetZip(zip, changeset);
            }

            zip.IsStreamOwner = true;
            zip.Close();
        }

        public bool VerifyBackup(string backupDir)
        {
            Console.WriteLine(Environment.NewLine + "Verifying backup integrity...");
            var fs = File.OpenRead(_changesets + "_rollback.zip");
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