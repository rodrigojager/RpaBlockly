using System.Security.Cryptography;
using Rpa.Worker.Configuration;
using Rpa.Worker.Domain;
using Rpa.Worker.Execution;

namespace Rpa.Worker.Storage;

public static class WorkerArtifactMaterializer
{
    public static async Task<IReadOnlyList<MaterializedArtifact>> MaterializeAsync(
        System.Text.Json.Nodes.JsonObject runtime,
        IReadOnlyList<ArtifactMappingOptions> mappings,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var result = new List<MaterializedArtifact>();
        foreach (var mapping in mappings)
        {
            if (!RuntimeOutputResolver.TryResolve(runtime, mapping.Source, out var node) ||
                node is null ||
                node.GetValueKind() == System.Text.Json.JsonValueKind.Null)
            {
                if (mapping.Required)
                {
                    throw new InvalidOperationException(
                        $"O artefato obrigatório '{mapping.Name}' não foi produzido em {mapping.Source}.");
                }

                continue;
            }

            if (node is not System.Text.Json.Nodes.JsonValue value ||
                !value.TryGetValue<string>(out var path) ||
                string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException(
                    $"O artefato '{mapping.Name}' precisa resolver para um caminho de arquivo.");
            }

            var absolutePath = Path.GetFullPath(
                Path.IsPathRooted(path) ? path : Path.Combine(workspaceRoot, path));
            if (!File.Exists(absolutePath))
            {
                throw new FileNotFoundException(
                    $"O caminho publicado para o artefato '{mapping.Name}' não existe.",
                    absolutePath);
            }

            await using var stream = File.OpenRead(absolutePath);
            var hash = Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken));
            result.Add(new MaterializedArtifact(
                mapping.Name,
                mapping.Kind,
                absolutePath,
                stream.Length,
                hash));
        }

        return result;
    }
}
