using System.Collections.Immutable;

namespace ApiOps.Core.FileManagement.Core;
public interface IWorkspace : IReadOnlyWorkspace
{
    (ImmutableArray<ISourceFile> added, ImmutableArray<ISourceFile> removed) UpsertSourceFiles(IEnumerable<ISourceFile> sourceFiles);

    void RemoveSourceFiles(IEnumerable<ISourceFile> sourceFiles);

}
