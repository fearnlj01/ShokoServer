
using System.Collections.Generic;

#nullable enable
namespace Shoko.Plugin.Abstractions.DataModels;

public interface IVideo : IMetadata<int>
{
    /// <summary>
    /// All video locations for the file.
    /// </summary>
    IReadOnlyList<IVideoFile> Locations { get; }

    /// <summary>
    /// The AniDB File Info. This will be null for manual links, which can reliably be used to tell if it was manually linked.
    /// </summary>
    IAniDBFile? AniDB { get; }

    /// <summary>
    /// The Relevant Hashes for a file. CRC should be the only thing used here, but clever uses of the API could use the others.
    /// </summary>
    IHashes Hashes { get; }

    /// <summary>
    /// The MediaInfo data for the file. This can be null, but it shouldn't be.
    /// </summary>
    IMediaContainer? MediaInfo { get; }

    /// <summary>
    /// All episodes linked to the video.
    /// </summary>
    IReadOnlyList<IEpisode> EpisodeInfo { get; }

    /// <summary>
    /// All shows linked to the show.
    /// </summary>
    IReadOnlyList<ISeries> SeriesInfo { get; }

    /// <summary>
    /// Information about the group
    /// </summary>
    IReadOnlyList<IGroup> GroupInfo { get; }
}