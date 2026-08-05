using System.Text;
using System.Text.Json;
using RpaFlow.Editor.Configuration;

namespace RpaFlow.Editor.Infrastructure;

public static class ProjectRootResolver
{
    private const string ProfileFileName = "rpa.editor.json";
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static EditorPaths Resolve(EditorArguments arguments)
    {
        var projectRoot = arguments.ProjectRoot is not null
            ? ValidateRoot(arguments.ProjectRoot)
            : FindRoot();
        var profileFile = Path.Combine(projectRoot, ProfileFileName);
        var profile = LoadProfile(profileFile);

        ResolveProjectFile(projectRoot, profile.ProjectFile);
        var configurationFile = ResolveExistingFile(
            projectRoot,
            arguments.ConfigurationFile ?? profile.ConfigurationFile);
        var flowFile = ResolveExistingFile(
            projectRoot,
            arguments.FlowFile ?? profile.FlowFile);

        return new EditorPaths(
            projectRoot,
            profileFile,
            profile,
            configurationFile,
            flowFile);
    }

    private static string FindRoot()
    {
        var starts = new[]
        {
            Directory.GetCurrentDirectory(),
            AppContext.BaseDirectory
        };

        foreach (var start in starts)
        {
            var directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, ProfileFileName)))
                {
                    return ValidateRoot(directory.FullName);
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "Não foi possível localizar rpa.editor.json. Use --project-root <caminho>.");
    }

    private static string ValidateRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var profileFile = Path.Combine(fullPath, ProfileFileName);
        if (!File.Exists(profileFile))
        {
            throw new DirectoryNotFoundException(
                $"A pasta não contém {ProfileFileName}: {fullPath}");
        }

        return fullPath;
    }

    private static EditorProfile LoadProfile(string profileFile)
    {
        var bytes = File.ReadAllBytes(profileFile);
        var profile = JsonSerializer.Deserialize<EditorProfile>(
            StrictUtf8.GetString(bytes),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("O perfil do editor está vazio.");
        profile.Validate();
        return profile;
    }

    private static string ResolveProjectFile(string projectRoot, string path)
    {
        var fullPath = ResolveInsideProject(projectRoot, path);
        if (!File.Exists(fullPath) ||
            !Path.GetExtension(fullPath).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException(
                $"Projeto .NET indicado pelo perfil não encontrado: {fullPath}",
                fullPath);
        }

        return fullPath;
    }

    private static string ResolveExistingFile(string projectRoot, string path)
    {
        var fullPath = ResolveInsideProject(projectRoot, path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"Arquivo necessário para o editor não encontrado: {fullPath}",
                fullPath);
        }

        return fullPath;
    }

    private static string ResolveInsideProject(string projectRoot, string path)
    {
        var fullPath = Path.GetFullPath(
            Path.IsPathRooted(path) ? path : Path.Combine(projectRoot, path));
        var relative = Path.GetRelativePath(projectRoot, fullPath);
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"O perfil do editor só pode apontar para arquivos dentro do RPA: {path}");
        }

        return fullPath;
    }
}
