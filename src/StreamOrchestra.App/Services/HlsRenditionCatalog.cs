using StreamOrchestra.App.Models;

namespace StreamOrchestra.App.Services;

/// <summary>
/// Keeps the in-memory master-to-child association graph for one navigation. Resource identities are
/// query-insensitive keyed hashes, so signed query rotation does not break the association.
/// </summary>
public sealed class HlsRenditionCatalog
{
    private readonly Dictionary<string, HlsRenditionKind> _children = new(StringComparer.Ordinal);

    public void Clear() => _children.Clear();

    public void Register(HlsPlaylistDocument master)
    {
        if (master.Kind != HlsPlaylistKind.Master)
        {
            return;
        }

        foreach (var variant in master.Variants)
        {
            RegisterChild(variant.Resource.PersistenceIdentity.PersistenceHash, HlsRenditionKind.Video);
        }

        foreach (var rendition in master.Renditions.Where(item => item.Resource is not null))
        {
            RegisterChild(rendition.Resource!.PersistenceIdentity.PersistenceHash, rendition.Kind);
        }
    }

    public HlsPlaylistParseResult Apply(HlsPlaylistParseResult result)
    {
        if (result.Document.Kind != HlsPlaylistKind.Media ||
            result.Document.RenditionKind != HlsRenditionKind.Unknown ||
            !_children.TryGetValue(
                result.Document.PlaylistIdentity.PersistenceHash,
                out var renditionKind) ||
            renditionKind == HlsRenditionKind.Unknown)
        {
            return result;
        }

        var document = result.Document with { RenditionKind = renditionKind };
        var timeline = result.TimelineCandidate is null
            ? null
            : result.TimelineCandidate with { RenditionKind = renditionKind };
        return result with { Document = document, TimelineCandidate = timeline };
    }

    private void RegisterChild(string identity, HlsRenditionKind kind)
    {
        if (string.IsNullOrWhiteSpace(identity))
        {
            return;
        }

        if (_children.TryGetValue(identity, out var existing) && existing != kind)
        {
            _children[identity] = HlsRenditionKind.Unknown;
            return;
        }

        _children[identity] = kind;
    }
}
