using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SafeSpeak.Core.Tests")]

namespace SafeSpeak.Core.AI;

/// <summary>
/// Runs SafeSpeak's pinned MiniLM toxicity classifier entirely on-device. The
/// deterministic heuristic remains active as a fallback and as an additional
/// signal for phrases where a small statistical model may be uncertain.
/// </summary>
public sealed class LocalOnnxIntentClassifier : IIntentClassifier
{
    private const string ModelSha256 =
        "935BA953C9D4478D809DB1A2FA40181F42BF1670D1E69261478B2137C1FBACC5";
    private const string TokenizerSha256 =
        "851CA67100D372CA3AE031A6ABD168F53489EEBFD7D89523F35C5C9B4D372C3C";
    private const int MaximumTokens = 256;

    private static readonly Regex PositiveProfanityRegex = new(
        @"^\s*(?:this|that|it|this\s+stream)\s+(?:is|was)\s+f+u+c+k+(?:i+n+g+)?\s+(?:amazing|awesome|excellent|fantastic|good|great|hilarious|incredible|perfect)\s*[.!?]*\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(50));

    private readonly HeuristicIntentClassifier _fallback = new();
    private readonly object _lifecycleLock = new();
    private RuntimeLease? _ownerLease;
    private bool _disposed;

    public string ModelName => "MiniLM Local Toxicity (ONNX)";
    public bool IsModelLoaded => GetRuntimeLoadResult().Runtime is not null;
    public string AvailabilityMessage
    {
        get
        {
            RuntimeLoadResult result = GetRuntimeLoadResult();
            return result.Runtime is not null
                ? "Local MiniLM moderation model is loaded"
                : $"Local model unavailable; deterministic fallback active. {result.Error}";
        }
    }

    internal static int ActiveRuntimeLeaseCount => SharedRuntimePool.ActiveLeaseCount;
    internal static bool HasActiveRuntime => SharedRuntimePool.HasActiveRuntime;
    internal static long RuntimeCreateCount => SharedRuntimePool.RuntimeCreateCount;
    internal static long RuntimeDisposeCount => SharedRuntimePool.RuntimeDisposeCount;

    public async Task<IntentClassificationResult> ClassifyAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        IntentClassificationResult fallback = await _fallback.ClassifyAsync(text, cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        using RuntimeLease operationLease = AcquireOperationLease();
        try
        {
            IntentClassificationResult? local = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return operationLease.LoadResult.Runtime?.Classify(text);
            }, cancellationToken);

            if (local is null)
            {
                return WithFallbackModelName(fallback);
            }

            local = ApplyPositiveContextPolicy(text, local, fallback);
            return Combine(local, fallback);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Missing model assets use the explicit fallback path above. An
            // inference failure for a specific message must instead fail closed
            // in ModerationPipeline rather than silently approving that input.
            throw new InvalidDataException("Local moderation inference failed.", ex);
        }
    }

    private static IntentClassificationResult ApplyPositiveContextPolicy(
        string text,
        IntentClassificationResult local,
        IntentClassificationResult fallback)
    {
        if (fallback.ToxicityScore >= 0.45 || !PositiveProfanityRegex.IsMatch(text))
        {
            return local;
        }

        // This narrow policy treats profanity immediately intensifying an
        // explicitly positive adjective as emphasis, not hostility. The rule
        // engine still runs first, so a broadcaster who bans the word itself
        // continues to block it regardless of context.
        return local with
        {
            IsToxic = false,
            ToxicityScore = Math.Min(local.ToxicityScore, 0.55),
            FlaggedCategory = "Positive emphatic language",
            ModelUsed = "MiniLM Local ONNX + positive-context policy"
        };
    }

    private static IntentClassificationResult Combine(
        IntentClassificationResult local,
        IntentClassificationResult fallback)
    {
        bool fallbackDominates = fallback.ToxicityScore >= local.ToxicityScore ||
            (fallback.FlaggedCategory != "None" && local.FlaggedCategory is "Toxic language" or "None");
        double toxicity = Math.Max(local.ToxicityScore, fallback.ToxicityScore);
        return new IntentClassificationResult
        {
            IsToxic = toxicity >= 0.60,
            ToxicityScore = toxicity,
            SevereToxicityScore = Math.Max(local.SevereToxicityScore, fallback.SevereToxicityScore),
            ObsceneScore = Math.Max(local.ObsceneScore, fallback.ObsceneScore),
            ThreatScore = Math.Max(local.ThreatScore, fallback.ThreatScore),
            HarassmentScore = Math.Max(local.HarassmentScore, fallback.HarassmentScore),
            InsultScore = Math.Max(local.InsultScore, fallback.InsultScore),
            IdentityHateScore = Math.Max(local.IdentityHateScore, fallback.IdentityHateScore),
            FlaggedCategory = fallbackDominates && fallback.FlaggedCategory != "None" ? fallback.FlaggedCategory : local.FlaggedCategory,
            ModelUsed = "MiniLM Local ONNX + deterministic heuristic"
        };
    }

    private static IntentClassificationResult WithFallbackModelName(IntentClassificationResult fallback) =>
        fallback with { ModelUsed = "Deterministic heuristic fallback (local model unavailable)" };

    private static RuntimeLoadResult LoadRuntime()
    {
        try
        {
            string modelDirectory = Path.Combine(AppContext.BaseDirectory, "Models", "Moderation");
            string modelPath = Path.Combine(modelDirectory, "model.onnx");
            string tokenizerPath = Path.Combine(modelDirectory, "tokenizer.json");

            if (!File.Exists(modelPath) || !File.Exists(tokenizerPath))
            {
                return new RuntimeLoadResult(null, "Bundled model assets were not found.");
            }

            VerifySha256(modelPath, ModelSha256);
            VerifySha256(tokenizerPath, TokenizerSha256);
            return new RuntimeLoadResult(
                new ModelRuntime(modelPath, tokenizerPath),
                null);
        }
        catch (Exception ex)
        {
            return new RuntimeLoadResult(null, ex.Message);
        }
    }

    private static void VerifySha256(string path, string expected)
    {
        using FileStream stream = File.OpenRead(path);
        string actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"The bundled moderation asset '{Path.GetFileName(path)}' failed checksum verification.");
        }
    }

    private RuntimeLoadResult GetRuntimeLoadResult()
    {
        lock (_lifecycleLock)
        {
            ThrowIfDisposed();
            _ownerLease ??= SharedRuntimePool.Acquire();
            return _ownerLease.LoadResult;
        }
    }

    private RuntimeLease AcquireOperationLease()
    {
        lock (_lifecycleLock)
        {
            ThrowIfDisposed();
            _ownerLease ??= SharedRuntimePool.Acquire();
            return SharedRuntimePool.Acquire();
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        RuntimeLease? ownerLease;
        lock (_lifecycleLock)
        {
            if (_disposed) return;
            _disposed = true;
            ownerLease = _ownerLease;
            _ownerLease = null;
        }

        ownerLease?.Dispose();
        _fallback.Dispose();
    }

    private sealed record RuntimeLoadResult(ModelRuntime? Runtime, string? Error);

    private sealed class RuntimeLease : IDisposable
    {
        private Action? _release;

        public RuntimeLoadResult LoadResult { get; }

        public RuntimeLease(RuntimeLoadResult loadResult, Action release)
        {
            LoadResult = loadResult;
            _release = release;
        }

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }

    private static class SharedRuntimePool
    {
        private static readonly object Gate = new();
        private static RuntimeLoadResult? _loadResult;
        private static int _activeLeaseCount;
        private static long _runtimeCreateCount;
        private static long _runtimeDisposeCount;

        public static int ActiveLeaseCount
        {
            get { lock (Gate) return _activeLeaseCount; }
        }

        public static bool HasActiveRuntime
        {
            get { lock (Gate) return _loadResult?.Runtime is not null; }
        }

        public static long RuntimeCreateCount
        {
            get { lock (Gate) return _runtimeCreateCount; }
        }

        public static long RuntimeDisposeCount
        {
            get { lock (Gate) return _runtimeDisposeCount; }
        }

        public static RuntimeLease Acquire()
        {
            lock (Gate)
            {
                if (_loadResult is null)
                {
                    _loadResult = LoadRuntime();
                    if (_loadResult.Runtime is not null) _runtimeCreateCount++;
                }

                _activeLeaseCount++;
                return new RuntimeLease(_loadResult, Release);
            }
        }

        private static void Release()
        {
            lock (Gate)
            {
                if (_activeLeaseCount <= 0)
                {
                    throw new InvalidOperationException("The local moderation runtime lease count is invalid.");
                }

                _activeLeaseCount--;
                if (_activeLeaseCount != 0) return;

                ModelRuntime? runtime = _loadResult?.Runtime;
                _loadResult = null;
                if (runtime is null) return;

                runtime.Dispose();
                _runtimeDisposeCount++;
            }
        }
    }

    private sealed class ModelRuntime : IDisposable
    {
        private static readonly string[] Labels =
            ["toxic", "severe_toxic", "obscene", "threat", "insult", "identity_hate"];

        private static readonly double[] PublishedThresholds =
            [0.50, 0.21, 0.44, 0.08, 0.32, 0.20];

        private readonly InferenceSession _session;
        private readonly BertWordPieceTokenizer _tokenizer;
        private int _disposed;

        public ModelRuntime(string modelPath, string tokenizerPath)
        {
            using var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4)
            };
            _session = new InferenceSession(modelPath, options);
            _tokenizer = BertWordPieceTokenizer.Load(tokenizerPath);

            if (!_session.InputMetadata.ContainsKey("input_ids") ||
                !_session.InputMetadata.ContainsKey("attention_mask"))
            {
                throw new InvalidDataException("The moderation model has an unsupported input contract.");
            }
        }

        public IntentClassificationResult Classify(string text)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            EncodedText encoded = _tokenizer.Encode(text, MaximumTokens);
            int length = encoded.InputIds.Length;
            var dimensions = new[] { 1, length };
            var inputIds = new DenseTensor<long>(encoded.InputIds, dimensions);
            var attentionMask = new DenseTensor<long>(encoded.AttentionMask, dimensions);
            var tokenTypeIds = new DenseTensor<long>(new long[length], dimensions);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask)
            };
            if (_session.InputMetadata.ContainsKey("token_type_ids"))
            {
                inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeIds));
            }

            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _session.Run(inputs);
            float[] logits = outputs.First().AsEnumerable<float>().ToArray();
            if (logits.Length < Labels.Length)
            {
                throw new InvalidDataException("The moderation model returned an unsupported output contract.");
            }

            var scores = new double[Labels.Length];
            for (int i = 0; i < scores.Length; i++)
            {
                if (!float.IsFinite(logits[i]))
                {
                    throw new InvalidDataException("The moderation model returned a non-finite score.");
                }
                double probability = Sigmoid(logits[i]);
                scores[i] = CalibrateToSharedScale(probability, PublishedThresholds[i]);
                if (!double.IsFinite(scores[i]) || scores[i] is < 0 or > 1)
                {
                    throw new InvalidDataException("The moderation model returned an invalid score.");
                }
            }

            int strongest = 0;
            for (int i = 1; i < scores.Length; i++)
            {
                if (scores[i] > scores[strongest]) strongest = i;
            }

            double total = scores.Max();
            return new IntentClassificationResult
            {
                IsToxic = total >= 0.60,
                ToxicityScore = total,
                SevereToxicityScore = scores[1],
                ObsceneScore = scores[2],
                ThreatScore = scores[3],
                HarassmentScore = Math.Max(scores[1], Math.Max(scores[4], scores[5])),
                InsultScore = scores[4],
                IdentityHateScore = scores[5],
                FlaggedCategory = ToDisplayCategory(Labels[strongest]),
                ModelUsed = "MiniLM Local ONNX"
            };
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _session.Dispose();
        }

        private static double Sigmoid(double value) =>
            value >= 0
                ? 1.0 / (1.0 + Math.Exp(-value))
                : Math.Exp(value) / (1.0 + Math.Exp(value));

        // Published per-label thresholds represent equivalent decision points.
        // Map each threshold to 0.60 so the product's four-level slider can use
        // one consistent scale across common and rare labels.
        private static double CalibrateToSharedScale(double probability, double threshold)
        {
            const double pivot = 0.60;
            double p = Math.Clamp(probability, 0.000001, 0.999999);
            double t = Math.Clamp(threshold, 0.000001, 0.999999);
            double adjustedLogit = Math.Log(p / (1.0 - p)) -
                                   Math.Log(t / (1.0 - t)) +
                                   Math.Log(pivot / (1.0 - pivot));
            return Sigmoid(adjustedLogit);
        }

        private static string ToDisplayCategory(string label) => label switch
        {
            "severe_toxic" => "Severe toxicity",
            "identity_hate" => "Identity hate",
            "obscene" => "Obscene or hostile language",
            "threat" => "Threat",
            "insult" => "Insult",
            _ => "Toxic language"
        };
    }

    private sealed record EncodedText(long[] InputIds, long[] AttentionMask);

    private sealed class BertWordPieceTokenizer
    {
        private readonly IReadOnlyDictionary<string, int> _vocabulary;
        private readonly int _unknownId;
        private readonly int _classificationId;
        private readonly int _separatorId;

        private BertWordPieceTokenizer(IReadOnlyDictionary<string, int> vocabulary)
        {
            _vocabulary = vocabulary;
            _unknownId = RequiredId("[UNK]");
            _classificationId = RequiredId("[CLS]");
            _separatorId = RequiredId("[SEP]");
        }

        public static BertWordPieceTokenizer Load(string tokenizerPath)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(tokenizerPath));
            JsonElement vocabularyJson = document.RootElement
                .GetProperty("model")
                .GetProperty("vocab");
            var vocabulary = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (JsonProperty token in vocabularyJson.EnumerateObject())
            {
                vocabulary[token.Name] = token.Value.GetInt32();
            }

            return new BertWordPieceTokenizer(vocabulary);
        }

        public EncodedText Encode(string text, int maximumTokens)
        {
            var ids = new List<long>(Math.Min(maximumTokens, 64)) { _classificationId };
            foreach (string token in BasicTokenize(text))
            {
                foreach (int piece in EncodeWord(token))
                {
                    if (ids.Count >= maximumTokens - 1) break;
                    ids.Add(piece);
                }
                if (ids.Count >= maximumTokens - 1) break;
            }
            ids.Add(_separatorId);

            long[] inputIds = ids.ToArray();
            long[] attentionMask = Enumerable.Repeat(1L, inputIds.Length).ToArray();
            return new EncodedText(inputIds, attentionMask);
        }

        private IEnumerable<int> EncodeWord(string token)
        {
            if (token.Length > 100)
            {
                yield return _unknownId;
                yield break;
            }

            int start = 0;
            var pieces = new List<int>();
            while (start < token.Length)
            {
                int end = token.Length;
                int? matchedId = null;
                int matchedEnd = start;
                while (end > start)
                {
                    string candidate = token[start..end];
                    if (start > 0) candidate = "##" + candidate;
                    if (_vocabulary.TryGetValue(candidate, out int id))
                    {
                        matchedId = id;
                        matchedEnd = end;
                        break;
                    }
                    end--;
                }

                if (!matchedId.HasValue)
                {
                    yield return _unknownId;
                    yield break;
                }

                pieces.Add(matchedId.Value);
                start = matchedEnd;
            }

            foreach (int piece in pieces) yield return piece;
        }

        private int RequiredId(string token) =>
            _vocabulary.TryGetValue(token, out int id)
                ? id
                : throw new InvalidDataException($"Tokenizer is missing required token {token}.");

        private static IEnumerable<string> BasicTokenize(string text)
        {
            string normalized = NormalizeForBert(text);
            var current = new StringBuilder();
            foreach (char character in normalized)
            {
                if (char.IsWhiteSpace(character))
                {
                    if (current.Length > 0)
                    {
                        yield return current.ToString();
                        current.Clear();
                    }
                    continue;
                }

                if (IsPunctuation(character))
                {
                    if (current.Length > 0)
                    {
                        yield return current.ToString();
                        current.Clear();
                    }
                    yield return character.ToString();
                    continue;
                }

                if (IsCjkCharacter(character))
                {
                    if (current.Length > 0)
                    {
                        yield return current.ToString();
                        current.Clear();
                    }
                    yield return character.ToString();
                    continue;
                }

                current.Append(character);
            }
            if (current.Length > 0) yield return current.ToString();
        }

        private static string NormalizeForBert(string text)
        {
            string decomposed = text.Normalize(NormalizationForm.FormD);
            var result = new StringBuilder(decomposed.Length);
            foreach (char character in decomposed)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
                if (category is UnicodeCategory.NonSpacingMark or
                    UnicodeCategory.SpacingCombiningMark or
                    UnicodeCategory.EnclosingMark or
                    UnicodeCategory.Control or
                    UnicodeCategory.Format)
                {
                    continue;
                }
                result.Append(char.ToLowerInvariant(character));
            }
            return result.ToString().Normalize(NormalizationForm.FormC);
        }

        private static bool IsPunctuation(char character)
        {
            if ((character >= '!' && character <= '/') ||
                (character >= ':' && character <= '@') ||
                (character >= '[' && character <= '`') ||
                (character >= '{' && character <= '~'))
            {
                return true;
            }

            return CharUnicodeInfo.GetUnicodeCategory(character) is
                UnicodeCategory.ConnectorPunctuation or
                UnicodeCategory.DashPunctuation or
                UnicodeCategory.OpenPunctuation or
                UnicodeCategory.ClosePunctuation or
                UnicodeCategory.InitialQuotePunctuation or
                UnicodeCategory.FinalQuotePunctuation or
                UnicodeCategory.OtherPunctuation;
        }

        // The bundled tokenizer enables BERT's handle_chinese_chars option,
        // which emits common BMP CJK ideographs as individual basic tokens.
        private static bool IsCjkCharacter(char character) =>
            character is >= '\u3400' and <= '\u4DBF' or
            >= '\u4E00' and <= '\u9FFF' or
            >= '\uF900' and <= '\uFAFF';
    }
}
