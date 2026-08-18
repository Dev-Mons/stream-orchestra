using System.Security.Cryptography;
using System.Text;
using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

public sealed record SyncBiasEstimatorOptions
{
    public int MinimumIndependentSessionSupport { get; init; } = 3;

    public TimeSpan Retention { get; init; } = TimeSpan.FromDays(180);

    public TimeSpan RecencyHalfLife { get; init; } = TimeSpan.FromDays(30);

    public double HuberDeltaMilliseconds { get; init; } = 500;

    public int MaximumSuggestionMagnitudeMilliseconds { get; init; } = 60000;
}

public sealed class SyncBiasEstimator
{
    private readonly SyncBiasEstimatorOptions _options;

    public SyncBiasEstimator(SyncBiasEstimatorOptions? options = null)
    {
        _options = options ?? new SyncBiasEstimatorOptions();
        if (_options.MinimumIndependentSessionSupport < 1 ||
            _options.Retention <= TimeSpan.Zero ||
            _options.RecencyHalfLife <= TimeSpan.Zero ||
            !double.IsFinite(_options.HuberDeltaMilliseconds) ||
            _options.HuberDeltaMilliseconds <= 0 ||
            _options.MaximumSuggestionMagnitudeMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    public SyncBiasSuggestion? Estimate(
        SyncBiasContext query,
        IReadOnlyList<SyncBiasPairObservation> observations,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(observations);
        foreach (var level in new[]
                 {
                     SyncBiasHierarchyLevel.ChannelQualityCdn,
                     SyncBiasHierarchyLevel.ChannelQuality,
                     SyncBiasHierarchyLevel.Channel
                 })
        {
            var estimate = EstimateAtLevel(query, observations, nowUtc.ToUniversalTime(), level);
            if (estimate is not null)
            {
                return estimate;
            }
        }

        return null;
    }

    private SyncBiasSuggestion? EstimateAtLevel(
        SyncBiasContext query,
        IReadOnlyList<SyncBiasPairObservation> observations,
        DateTimeOffset nowUtc,
        SyncBiasHierarchyLevel level)
    {
        var cutoff = nowUtc - _options.Retention;
        var projected = observations
            .Where(IsEligible)
            .Where(observation => observation.OccurredAtUtc.ToUniversalTime() >= cutoff &&
                                  observation.OccurredAtUtc.ToUniversalTime() <= nowUtc)
            .Select(observation => Project(observation, level, nowUtc))
            .Where(edge => edge.Left != edge.Right)
            .GroupBy(edge => $"{edge.SessionHash}:{edge.UnorderedPairKey}", StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(edge => edge.OccurredAtUtc).First())
            .ToArray();
        var queryNode = NodeKey(query, level);
        var componentNodes = FindComponent(queryNode, projected);
        if (componentNodes.Count < 2)
        {
            return null;
        }

        var componentEdges = projected
            .Where(edge => componentNodes.Contains(edge.Left) && componentNodes.Contains(edge.Right))
            .ToArray();
        var sessionSupport = componentEdges
            .Select(edge => edge.SessionHash)
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (sessionSupport < _options.MinimumIndependentSessionSupport)
        {
            return null;
        }

        var solution = Solve(componentNodes, componentEdges);
        if (!solution.Values.TryGetValue(queryNode, out var delay) || !double.IsFinite(delay))
        {
            return null;
        }

        var rounded = (int)Math.Round(delay / 100, MidpointRounding.AwayFromZero) * 100;
        rounded = Math.Clamp(
            rounded,
            -_options.MaximumSuggestionMagnitudeMilliseconds,
            _options.MaximumSuggestionMagnitudeMilliseconds);
        var supportScore = Math.Min(1, sessionSupport /
            (double)(_options.MinimumIndependentSessionSupport * 2));
        var precisionScore = 1 / (1 + solution.ResidualScaleMilliseconds / 1000);
        var componentId = Hash(string.Join("|", componentNodes.Order(StringComparer.Ordinal)));
        return new SyncBiasSuggestion
        {
            SuggestionId = Hash($"{level}:{componentId}:{queryNode}:{rounded}"),
            SuggestedDelayMilliseconds = rounded,
            HierarchyLevel = level,
            ComponentId = componentId,
            IndependentSessionSupport = sessionSupport,
            DiagnosticConfidenceScore = Math.Clamp(supportScore * precisionScore, 0, 1),
            ResidualScaleMilliseconds = solution.ResidualScaleMilliseconds,
            IsSuggestionOnly = true
        };
    }

    private Solution Solve(IReadOnlySet<string> componentNodes, IReadOnlyList<Edge> edges)
    {
        var nodes = componentNodes.Order(StringComparer.Ordinal).ToArray();
        var nodeIndex = nodes.Select((node, index) => (node, index))
            .ToDictionary(item => item.node, item => item.index, StringComparer.Ordinal);
        var robustWeights = Enumerable.Repeat(1d, edges.Count).ToArray();
        var values = new double[nodes.Length];
        for (var iteration = 0; iteration < 4; iteration++)
        {
            values = SolveWeighted(nodes, nodeIndex, edges, robustWeights);
            for (var edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
            {
                var edge = edges[edgeIndex];
                var residual = values[nodeIndex[edge.Left]] - values[nodeIndex[edge.Right]] -
                               edge.DifferenceMilliseconds;
                var absoluteResidual = Math.Abs(residual);
                robustWeights[edgeIndex] = edge.RecencyWeight *
                    (absoluteResidual <= _options.HuberDeltaMilliseconds
                        ? 1
                        : _options.HuberDeltaMilliseconds / absoluteResidual);
            }
        }

        var residuals = edges.Select(edge =>
            values[nodeIndex[edge.Left]] - values[nodeIndex[edge.Right]] -
            edge.DifferenceMilliseconds).ToArray();
        var residualScale = residuals.Length == 0
            ? 0
            : 1.4826 * Median(residuals.Select(Math.Abs));
        return new Solution(
            nodes.Select((node, index) => (node, value: values[index]))
                .ToDictionary(item => item.node, item => item.value, StringComparer.Ordinal),
            residualScale);
    }

    private static double[] SolveWeighted(
        IReadOnlyList<string> nodes,
        IReadOnlyDictionary<string, int> nodeIndex,
        IReadOnlyList<Edge> edges,
        IReadOnlyList<double> weights)
    {
        var dimension = nodes.Count - 1;
        var matrix = new double[dimension, dimension];
        var rightHandSide = new double[dimension];
        for (var edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
        {
            var edge = edges[edgeIndex];
            var left = nodeIndex[edge.Left];
            var right = nodeIndex[edge.Right];
            var weight = Math.Max(0.000001, weights[edgeIndex]);
            if (left < dimension)
            {
                matrix[left, left] += weight;
                rightHandSide[left] += weight * edge.DifferenceMilliseconds;
            }

            if (right < dimension)
            {
                matrix[right, right] += weight;
                rightHandSide[right] -= weight * edge.DifferenceMilliseconds;
            }

            if (left < dimension && right < dimension)
            {
                matrix[left, right] -= weight;
                matrix[right, left] -= weight;
            }
        }

        for (var index = 0; index < dimension; index++)
        {
            matrix[index, index] += 0.000000001;
        }

        var reduced = GaussianElimination(matrix, rightHandSide);
        var values = new double[nodes.Count];
        Array.Copy(reduced, values, reduced.Length);
        var mean = values.Average();
        for (var index = 0; index < values.Length; index++)
        {
            values[index] -= mean;
        }

        return values;
    }

    private static double[] GaussianElimination(double[,] matrix, double[] rightHandSide)
    {
        var size = rightHandSide.Length;
        var augmented = new double[size, size + 1];
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                augmented[row, column] = matrix[row, column];
            }
            augmented[row, size] = rightHandSide[row];
        }

        for (var pivot = 0; pivot < size; pivot++)
        {
            var bestRow = Enumerable.Range(pivot, size - pivot)
                .MaxBy(row => Math.Abs(augmented[row, pivot]));
            if (Math.Abs(augmented[bestRow, pivot]) < 0.000000000001)
            {
                continue;
            }

            if (bestRow != pivot)
            {
                for (var column = pivot; column <= size; column++)
                {
                    (augmented[pivot, column], augmented[bestRow, column]) =
                        (augmented[bestRow, column], augmented[pivot, column]);
                }
            }

            var divisor = augmented[pivot, pivot];
            for (var column = pivot; column <= size; column++)
            {
                augmented[pivot, column] /= divisor;
            }

            for (var row = 0; row < size; row++)
            {
                if (row == pivot)
                {
                    continue;
                }

                var factor = augmented[row, pivot];
                for (var column = pivot; column <= size; column++)
                {
                    augmented[row, column] -= factor * augmented[pivot, column];
                }
            }
        }

        return Enumerable.Range(0, size).Select(row => augmented[row, size]).ToArray();
    }

    private Edge Project(
        SyncBiasPairObservation observation,
        SyncBiasHierarchyLevel level,
        DateTimeOffset nowUtc)
    {
        var left = NodeKey(observation.Left, level);
        var right = NodeKey(observation.Right, level);
        var difference = observation.DelayDifferenceMilliseconds;
        if (string.CompareOrdinal(left, right) > 0)
        {
            (left, right) = (right, left);
            difference = -difference;
        }

        var age = Math.Max(0, (nowUtc - observation.OccurredAtUtc.ToUniversalTime()).TotalSeconds);
        var halfLifeSeconds = _options.RecencyHalfLife.TotalSeconds;
        var recencyWeight = Math.Exp(-Math.Log(2) * age / halfLifeSeconds);
        return new Edge(
            left,
            right,
            difference,
            observation.IndependentSessionHash,
            $"{left}|{right}",
            observation.OccurredAtUtc,
            recencyWeight);
    }

    private static HashSet<string> FindComponent(string queryNode, IReadOnlyList<Edge> edges)
    {
        var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            Add(edge.Left, edge.Right);
            Add(edge.Right, edge.Left);
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(queryNode);
        while (queue.TryDequeue(out var node) && visited.Add(node))
        {
            if (adjacency.TryGetValue(node, out var neighbors))
            {
                foreach (var neighbor in neighbors)
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        return visited;

        void Add(string from, string to)
        {
            if (!adjacency.TryGetValue(from, out var neighbors))
            {
                neighbors = new HashSet<string>(StringComparer.Ordinal);
                adjacency[from] = neighbors;
            }
            neighbors.Add(to);
        }
    }

    private static string NodeKey(SyncBiasContext context, SyncBiasHierarchyLevel level) =>
        level switch
        {
            SyncBiasHierarchyLevel.ChannelQualityCdn =>
                $"{context.StableChannelHash}|{context.QualityBucket}|{context.CdnBucket}",
            SyncBiasHierarchyLevel.ChannelQuality =>
                $"{context.StableChannelHash}|{context.QualityBucket}",
            _ => context.StableChannelHash
        };

    private static bool IsEligible(SyncBiasPairObservation observation) =>
        observation.EventKind == SyncBiasManualEventKind.AlignmentConfirmed &&
        observation.IsIndependentSession &&
        observation.IsStableFinal &&
        !string.IsNullOrWhiteSpace(observation.IndependentSessionHash) &&
        !string.IsNullOrWhiteSpace(observation.Left.StableChannelHash) &&
        !string.IsNullOrWhiteSpace(observation.Right.StableChannelHash) &&
        double.IsFinite(observation.DelayDifferenceMilliseconds);

    private static double Median(IEnumerable<double> source)
    {
        var values = source.OrderBy(value => value).ToArray();
        if (values.Length == 0)
        {
            return 0;
        }

        var middle = values.Length / 2;
        return values.Length % 2 == 0
            ? (values[middle - 1] + values[middle]) / 2
            : values[middle];
    }

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24];

    private sealed record Edge(
        string Left,
        string Right,
        double DifferenceMilliseconds,
        string SessionHash,
        string UnorderedPairKey,
        DateTimeOffset OccurredAtUtc,
        double RecencyWeight);

    private sealed record Solution(
        IReadOnlyDictionary<string, double> Values,
        double ResidualScaleMilliseconds);
}
