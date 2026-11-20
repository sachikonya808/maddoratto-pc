using RattoLauncher.Core.Models;
using RattoLauncher.App.Class;
using System.IO.Compression;
using System.Text.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

namespace RattoLauncher.Core.Services
{
    public class SongPackageScanner
    {
        public List<SongPackage> Scan(string folderPath)
        {
            var packages = new List<SongPackage>();
            if (!Directory.Exists(folderPath)) { return packages; }

            foreach(string zipPath in Directory.EnumerateFiles(folderPath, "*.zip", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    using var archive = ZipFile.OpenRead(zipPath);

                    var imageEntry = archive.Entries.FirstOrDefault(e => Path.GetFileName(e.FullName).Equals("pack.png", StringComparison.OrdinalIgnoreCase));
                    var jsonEntry = archive.Entries.FirstOrDefault(e => Path.GetFileName(e.FullName).Equals("pack.json", StringComparison.OrdinalIgnoreCase));

                    if (jsonEntry == null || imageEntry  == null) { continue; }

                    SongMetadata? metadata;

                    using (var jsonStream = jsonEntry.Open())
                    {
                        metadata = JsonSerializer.Deserialize<SongMetadata>(jsonStream);
                    }

                    if(metadata == null) { continue; }

                    byte[] imageData;

                    using(var imageStream = imageEntry.Open())
                    using(var memoryStream = new MemoryStream())
                    {
                        imageStream.CopyTo(memoryStream);
                        imageData = memoryStream.ToArray();
                    }

                    var package = new SongPackage
                    {
                        ZipPath = zipPath,
                        Title = string.IsNullOrWhiteSpace(metadata.Title) ? Path.GetFileNameWithoutExtension(zipPath) : metadata.Title,
                        Composer = metadata.Composer ?? "Unknown Artist",
                        BPM = $"BPM: {metadata.BPM}" ?? "?",
                        BaseID = $"Base ID: {metadata.BaseID}" ?? "?",
                        ImageData = imageData
                    };

                    packages.Add(package);
                } 
                catch (InvalidDataException) {  }
                catch (JsonException) {  }
                catch (IOException) {  }
            }

            return packages;
        }
    }
}
