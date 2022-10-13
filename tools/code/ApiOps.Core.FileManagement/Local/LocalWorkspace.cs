using ApiOps.Core.FileManagement.Core;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace ApiOps.Core.FileManagement.Local;
public class LocalWorkspace : IWorkspace
{
    private readonly IDictionary<Uri, ISourceFile> activeFiles = new Dictionary<Uri, ISourceFile>();

    public bool TryGetSourceFile(Uri fileUri, [NotNullWhen(true)] out ISourceFile? file)
    {
        return activeFiles.TryGetValue(fileUri, out file);
    }

    public IEnumerable<ISourceFile> GetSourceFilesForDirectory(Uri fileUri)
    {
        return activeFiles
                .Where(kvp => fileUri.IsBaseOf(kvp.Key))
                .Select(kvp => kvp.Value);
    }

    public ImmutableDictionary<Uri, ISourceFile> GetActiveSourceFilesByUri()
    {
        return activeFiles.ToImmutableDictionary();
    }

    public (ImmutableArray<ISourceFile> added, ImmutableArray<ISourceFile> removed) UpsertSourceFiles(IEnumerable<ISourceFile> files)
    {
        List<ISourceFile> added = new();
        List<ISourceFile> removed = new();

        foreach (ISourceFile newFile in files)
        {
            if (activeFiles.TryGetValue(newFile.FileUri, out ISourceFile? oldFile))
            {
                if (oldFile == newFile)
                {
                    continue;
                }

                removed.Add(oldFile);
            }

            added.Add(newFile);

            activeFiles[newFile.FileUri] = newFile;
        }

        return (added.ToImmutableArray(), removed.ToImmutableArray());
    }

    public void RemoveSourceFiles(IEnumerable<ISourceFile> files)
    {
        foreach (ISourceFile file in files)
        {
            if (activeFiles.TryGetValue(file.FileUri, out ISourceFile? treeToRemove) && treeToRemove == file)
            {
                _ = activeFiles.Remove(file.FileUri);
            }
        }
    }
}
