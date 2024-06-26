using System;

#nullable enable
namespace Shoko.Plugin.Abstractions.DataModels;

public interface IVideoFile
{
    /// <summary>
    /// The video file location id.
    /// </summary>
    int ID { get; }

    /// <summary>
    /// The video id.
    /// </summary>
    int VideoID { get; }

    /// <summary>
    /// The import folder id.
    /// </summary>
    int ImportFolderID { get; }

    /// <summary>
    /// The file name.
    /// </summary>
    string FileName { get; }

    /// <summary>
    /// The absolute path leading to the location of the file. Uses an OS dependent directory seperator.
    /// </summary>
    string Path { get; }

    /// <summary>
    /// The relative path from the <see cref="ImportFolder"/> to the location of the file. Will always use forward slash as a directory seperator.
    /// </summary>
    string RelativePath { get; }

    /// <summary>
    /// The file size, in bytes.
    /// </summary>
    long Size { get; }

    /// <summary>
    /// Get the video tied to the video file location.
    /// </summary>
    /// <value></value>
    IVideo? VideoInfo { get; }

    /// <summary>
    /// The import folder tied to the video file location.
    /// </summary>
    IImportFolder? ImportFolder { get; }

    #region To-be-removed

    [Obsolete("Use VideoID instead.")]
    int VideoFileID { get; }

    [Obsolete("Use FileName instead. Change the 'n' to a 'N' and you're good mate.")]
    string Filename { get; }

    [Obsolete("Use Path instead.")]
    string FilePath { get; }

    [Obsolete("Use Size instead.")]
    long FileSize { get; }

    [Obsolete("Use VideoInfo?.Hashes instead.")]
    IHashes? Hashes { get; }

    [Obsolete("Use VideoInfo?.MediaInfo instead")]
    IMediaContainer? MediaInfo { get; }

    [Obsolete("Use VideoInfo?.AniDB instead.")]
    IAniDBFile? AniDBFileInfo { get; }

    #endregion
}
