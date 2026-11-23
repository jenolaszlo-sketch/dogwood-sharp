using System.IO.Compression;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: PackageNormalizer <nupkg> [<nupkg> ...]");
    return 2;
}

var timestamp = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
foreach (var argument in args)
{
    var packagePath = Path.GetFullPath(argument);
    if (!File.Exists(packagePath))
    {
        Console.Error.WriteLine($"Package does not exist: {packagePath}");
        return 3;
    }

    var temporaryPath = Path.Combine(
        Path.GetDirectoryName(packagePath)!,
        $".{Path.GetFileName(packagePath)}.{Guid.NewGuid():N}.normalize.tmp");
    try
    {
        using (var source = ZipFile.OpenRead(packagePath))
        using (var destinationStream = new FileStream(
            temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        using (var destination = new ZipArchive(destinationStream, ZipArchiveMode.Create))
        {
            foreach (var sourceEntry in source.Entries)
            {
                var destinationEntry = destination.CreateEntry(
                    sourceEntry.FullName, CompressionLevel.Optimal);
                destinationEntry.LastWriteTime = timestamp;
                destinationEntry.ExternalAttributes = sourceEntry.ExternalAttributes;
                using var sourceStream = sourceEntry.Open();
                using var destinationEntryStream = destinationEntry.Open();
                sourceStream.CopyTo(destinationEntryStream);
            }
        }

        File.Move(temporaryPath, packagePath, overwrite: true);
        Console.WriteLine($"Normalized package timestamps: {Path.GetFileName(packagePath)}");
    }
    catch
    {
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }

        throw;
    }
}

return 0;
