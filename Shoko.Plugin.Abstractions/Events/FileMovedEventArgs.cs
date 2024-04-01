using System;
using System.Collections.Generic;
using Shoko.Plugin.Abstractions.DataModels;

#nullable enable
namespace Shoko.Plugin.Abstractions;

public class FileMovedEventArgs : FileEventArgs
{
    /// <summary>
    /// The previous relative path of the file from the
    /// <see cref="PreviousImportFolder"/>'s base location.
    /// </summary>
    public string PreviousRelativePath { get; set; }

    /// <summary>
    /// The previous import folder that the file was in.
    /// </summary>
    public IImportFolder PreviousImportFolder { get; set; }

    #region To-be-removed

    [Obsolete("Use ImportFolder instead.")]
    public IImportFolder NewImportFolder => ImportFolder;

    [Obsolete("Use RelativePath instead.")]
    public string NewRelativePath => RelativePath;

    [Obsolete("Use PreviousImportFolder instead.")]
    public IImportFolder OldImportFolder => PreviousImportFolder;

    [Obsolete("Use PreviousRelativePath instead.")]
    public string OldRelativePath => PreviousRelativePath;

    #endregion

    public FileMovedEventArgs(string relativePath, IImportFolder importFolder, string previousRelativePath, IImportFolder previousImportFolder, IVideoFile fileInfo, IVideo videoInfo, IEnumerable<IEpisode> episodeInfo, IEnumerable<IAnime> animeInfo, IEnumerable<IGroup> groupInfo)
        : base(relativePath, importFolder, fileInfo, videoInfo, episodeInfo, animeInfo, groupInfo)
    {
        PreviousRelativePath = previousRelativePath;
        PreviousImportFolder = previousImportFolder;
    }
}
