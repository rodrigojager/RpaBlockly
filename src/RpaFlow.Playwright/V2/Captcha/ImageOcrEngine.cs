using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace RpaFlow.Playwright.V2;

/// <summary>
/// OCR embutido para captchas de imagem/texto usando o modelo ddddocr
/// (common.onnx). Execute via <see cref="OcrModelResolver"/>, que destrava a
/// dependência OPCIONAL de Microsoft.ML.OnnxRuntime.
/// </summary>
internal sealed class ImageOcrEngine : IDisposable
{
    private readonly CachedOcrModel _model;
    private readonly string[] _charset;

    public ImageOcrEngine(string modelPath)
    {
        var descriptor = OcrModelResolver.Validate(modelPath);
        _model = OcrSessionCache.Get(Path.GetFullPath(modelPath), descriptor);
        _charset = OcrCharset.Get();
        if (_charset.Length != descriptor.Charset.Classes ||
            descriptor.Charset.BlankIndex != 0 ||
            descriptor.Preprocessing.CtcBlankIndex != 0)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ModelMismatch,
                "O charset embutido não corresponde ao manifesto do modelo OCR.");
        }
    }

    public string ModelVersion => _model.Descriptor.Version;

    /// <summary>
    /// Reconhece texto a partir de pixels RGBA crus (extraídos via canvas da página).
    /// </summary>
    public string Recognize(byte[] rgba, int width, int height)
    {
        var pixelCount = CaptchaPixels.ValidateRgba(rgba, width, height);
        var gray = CaptchaPixels.ToGrayscale(rgba, pixelCount, compositeOnWhite: true);
        var (targetWidth, samples) = CaptchaPixels.ResizeAndNormalize(
            gray,
            width,
            height,
            _model.Descriptor.Preprocessing.TargetHeight);

        var tensor = new DenseTensor<float>(
            samples,
            [1, 1, _model.Descriptor.Preprocessing.TargetHeight, targetWidth]);
        try
        {
            _model.InferenceGate.Wait();
            try
            {
                using var results = _model.Session.Run(
                    [NamedOnnxValue.CreateFromTensor(
                        _model.Descriptor.Tensors.InputName,
                        tensor)]);
                var output = results.Single().AsTensor<long>();
                if (output.Length is < 1 or > 4_096)
                {
                    throw new CaptchaException(
                        CaptchaErrorCodes.ModelMismatch,
                        $"A saída OCR possui tamanho inesperado: {output.Length}.");
                }

                return CtcDecoder.Decode(output.ToArray(), _charset);
            }
            finally
            {
                _model.InferenceGate.Release();
            }
        }
        catch (CaptchaException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is OnnxRuntimeException or InvalidOperationException)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ModelMismatch,
                "O modelo OCR não respeitou o contrato de tensores fixado no manifesto.",
                innerException: exception);
        }
    }

    // A sessão é compartilhada por modelo e liberada no encerramento do processo.
    public void Dispose()
    {
    }
}

internal static class OcrSessionCache
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, CacheEntry> Entries =
        new(StringComparer.OrdinalIgnoreCase);

    static OcrSessionCache()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeAll();
    }

    public static CachedOcrModel Get(string modelPath, OcrModelDescriptor descriptor)
    {
        var info = new FileInfo(modelPath);
        var signature = $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";
        lock (Sync)
        {
            if (Entries.TryGetValue(modelPath, out var existing) &&
                existing.Signature.Equals(signature, StringComparison.Ordinal))
            {
                return existing.Model;
            }

            existing?.Model.Dispose();
            var model = new CachedOcrModel(modelPath, descriptor);
            Entries[modelPath] = new CacheEntry(signature, model);
            return model;
        }
    }

    internal static void DisposeAll()
    {
        lock (Sync)
        {
            foreach (var entry in Entries.Values)
            {
                entry.Model.Dispose();
            }

            Entries.Clear();
        }
    }

    private sealed record CacheEntry(string Signature, CachedOcrModel Model);
}

internal sealed class CachedOcrModel : IDisposable
{
    public CachedOcrModel(string modelPath, OcrModelDescriptor descriptor)
    {
        Descriptor = descriptor;
        var options = new SessionOptions
        {
            InterOpNumThreads = 1,
            IntraOpNumThreads = Math.Max(1, Math.Min(Environment.ProcessorCount, 2)),
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
        };
        Session = new InferenceSession(modelPath, options);
        ValidateMetadata();
    }

    public InferenceSession Session { get; }

    public OcrModelDescriptor Descriptor { get; }

    public SemaphoreSlim InferenceGate { get; } = new(1, 1);

    private void ValidateMetadata()
    {
        if (Session.InputMetadata.Count != 1 ||
            !Session.InputMetadata.TryGetValue(Descriptor.Tensors.InputName, out var input) ||
            input.ElementType != typeof(float) ||
            input.Dimensions.Length != 4 ||
            input.Dimensions[0] != 1 ||
            input.Dimensions[1] != Descriptor.Preprocessing.Channels ||
            input.Dimensions[2] != Descriptor.Preprocessing.TargetHeight)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ModelMismatch,
                "A entrada ONNX não corresponde a input1 float32 [1,1,64,width].");
        }

        if (Session.OutputMetadata.Count != 1 ||
            !Session.OutputMetadata.TryGetValue(Descriptor.Tensors.OutputName, out var output) ||
            output.ElementType != typeof(long) ||
            output.Dimensions.Length != 2 ||
            output.Dimensions[0] != 1)
        {
            throw new CaptchaException(
                CaptchaErrorCodes.ModelMismatch,
                "A saída ONNX não corresponde a output int64 [1,sequence].");
        }
    }

    public void Dispose()
    {
        Session.Dispose();
        InferenceGate.Dispose();
    }
}
