using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace ApiOps.Core.FileManagement.Core;
public interface IReadOnlyWorkspace
{
    bool TryGetSourceFile(Uri fileUri, [NotNullWhen(true)] out ISourceFile? sourceFile);

    IEnumerable<ISourceFile> GetSourceFilesForDirectory(Uri fileUri);

    ImmutableDictionary<Uri, ISourceFile> GetActiveSourceFilesByUri();
}
