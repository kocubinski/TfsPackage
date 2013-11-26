using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Microsoft.TeamFoundation.VersionControl.Client;

namespace TfsPackage
{
    public class TfsChangesets : IEnumerable<Changeset>
    {
        public static TfsChangesets FromString(string sChangesets)
        {
            var changesets = new TfsChangesets();
            if (sChangesets.Contains(","))
            {
                // csv changesets
                changesets.ChangesetIds = sChangesets.Split(',').Select(int.Parse)
                                                     .OrderByDescending(i => i)
                                                     .ToList();
            }
            else if (sChangesets.Contains("~"))
            {
                // range of changesets
                var range = sChangesets.Split('~');
                if (range.Length != 2)
                    throw new ArgumentException("Need 2 changesets ids for a range.");
                changesets.IsRange = true;
                changesets.ChangesetIds = range.Select(int.Parse).ToList();
            }
            else
            {
                // single changset
                changesets.ChangesetIds = new List<int> {int.Parse(sChangesets)};
            }

            return changesets;
        }

        public bool IsRange { get; set; }

        public IList<int> ChangesetIds { get; set; }

        public IEnumerable<Changeset> Changesets { get; private set; }

        public void LoadChangesets(VersionControlServer vc, string serverFolder)
        {
            if (!IsRange)
            {
                Changesets = ChangesetIds.Select(vc.GetChangeset);
                return;
            }

            Changesets = vc.QueryHistory(
                new QueryHistoryParameters(serverFolder, RecursionType.Full)
                    {
                        VersionStart = new ChangesetVersionSpec(ChangesetIds[0]),
                        VersionEnd = new ChangesetVersionSpec(ChangesetIds[1]),
                        IncludeChanges = true
                    }
                );
        }

        public override string ToString()
        {
            if (IsRange)
            {
                return ChangesetIds[0] + "~" + ChangesetIds[1];
            }

            string res = ChangesetIds.First().ToString();
            res = ChangesetIds.Skip(1).Aggregate(res, (c, changeset) => c + ("_" + changeset));
            return res;
        }

        public IEnumerator<Changeset> GetEnumerator()
        {
            return Changesets.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}