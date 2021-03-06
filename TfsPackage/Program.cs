﻿using System;
using System.Linq;
using System.Collections.Generic;
using CommandLine;
using log4net.Config;

namespace TfsPackage
{
    class Program
    {
        public class Options
        {
            [Option('c', "changeset", Required = true, HelpText = "Target changset")]
            public string Changesets { get; set; }

            [Option('r', "root", Required = false)]
            public string Root { get; set; }

            [Option('b', "backup", Required = false, DefaultValue = "")]
            public string Backup { get; set; }

            [Option('e', "exclude", Required = false, DefaultValue = "")]
            public string Exclude { get; set; }

            [Option("verify", DefaultValue = true)]
            public bool Verify { get; set; }
        }


        static public void Main(string[] args)
        {
            XmlConfigurator.Configure();

            var options = new Options();
            Parser.Default.ParseArguments(args, options);
            if (string.IsNullOrEmpty(options.Root))
                options.Root = Environment.CurrentDirectory;

            var changesets = TfsChangesets.FromString(options.Changesets);
            var packager = new ElPackager(options.Root, changesets);

            string exclude = options.Exclude;
            if (exclude != string.Empty)
            {
                packager.ExcludedItems = exclude.Contains(",")
                                             ? exclude.Split(',')
                                             : new[] {exclude};
            }

            if (!string.IsNullOrEmpty(options.Backup))
                packager.ZipBackup(options.Backup);

            if (!string.IsNullOrEmpty(options.Backup))
                packager.BuildDeleteScript(options.Backup);

            packager.ZipChangesets();


            if (packager.VerifyBackup(options.Backup))
            {
                Console.WriteLine("Complete!");
                Console.ReadLine();
            }
            else
            {
                Console.WriteLine("There was a problem, backup not the same as files on disk!");
                Console.ReadLine();
            }
        }
    }
}
