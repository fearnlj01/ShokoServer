using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Web;
using F23.StringSimilarity;
using F23.StringSimilarity.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shoko.Commons.Extensions;
using Shoko.Models.Enums;
using Shoko.Server.AniDB_API.Titles;
using Shoko.Server.API.Annotations;
using Shoko.Server.API.v3.Models.Common;
using Shoko.Server.API.v3.Models.Shoko;
using Shoko.Server.Extensions;
using Shoko.Server.Models;
using Shoko.Server.Repositories;
using Shoko.Server.Settings;

namespace Shoko.Server.API.v3.Controllers
{
    [ApiController, Route("/api/v{version:apiVersion}/[controller]"), ApiV3]
    [Authorize]
    public class SeriesController : BaseController
    {
        #region Return messages

        internal static string SeriesNotFoundWithSeriesID = "No Series entry for the given seriesID";

        internal static string SeriesNotFoundWithAnidbID = "No Series entry for the given anidbID";

        internal static string SeriesForbiddenForUser = "Accessing Series is not allowed for the current user";

        internal static string AnidbNotFoundForSeriesID = "No Series.AniDB entry for the given seriesID";

        internal static string AnidbNotFoundForAnidbID = "No Series.AniDB entry for the given anidbID";

        internal static string AnidbForbiddenForUser = "Accessing Series.AniDB is not allowed for the current user";

        internal static string TvdbNotFoundForSeriesID = "No Series.TvDB entry for the given seriesID";

        internal static string TvdbNotFoundForTvdbID = "No Series.TvDB entry for the given tvdbID";

        internal static string TvdbForbiddenForUser = "Accessing Series.TvDB is not allowed for the current user";

        #endregion
        #region Metadata
        #region Shoko

        /// <summary>
        /// Get a paginated list of all <see cref="Series"/> available to the current <see cref="User"/>.
        /// </summary>
        /// <param name="page">The page index.</param>
        /// <param name="pageSize">The page size.</param>
        /// <returns></returns>
        [HttpGet]
        public ActionResult<List<Series>> GetAllSeries([FromQuery] int page = 0, [FromQuery] int pageSize = 50)
        {
            var series = RepoFactory.AnimeSeries.GetAll()
                .Where(a => User.AllowedSeries(a))
                .OrderBy(a => a.GetSeriesName());

            if (pageSize <= 0)
                return series
                    .Select(a => new Series(HttpContext, a))
                    .ToList();

            if (page <= 0) page = 0;
            return series
                .Skip(page * pageSize)
                .Take(pageSize)
                .Select(a => new Series(HttpContext, a))
                .ToList();
        }

        /// <summary>
        /// Get the series with ID
        /// </summary>
        /// <param name="seriesID">Shoko ID</param>
        /// <returns></returns>
        [HttpGet("{seriesID}")]
        public ActionResult<Series> GetSeries([FromRoute] int seriesID)
        {
            var series = RepoFactory.AnimeSeries.GetByID(seriesID);
            if (series == null)
                return NotFound(SeriesNotFoundWithSeriesID);
            if (!User.AllowedSeries(series))
                return Forbid(SeriesForbiddenForUser);

            return new Series(HttpContext, series);
        }

        /// <summary>
        /// Delete a Series
        /// </summary>
        /// <param name="seriesID">The ID of the Series</param>
        /// <param name="deleteFiles">Whether to delete all of the files in the series from the disk.</param>
        /// <returns></returns>
        [Authorize("admin")]
        [HttpDelete("{seriesID}")]
        public ActionResult DeleteSeries([FromRoute] int seriesID, [FromQuery] bool deleteFiles = false)
        {
            var series = RepoFactory.AnimeSeries.GetByID(seriesID);
            if (series == null)
                return NotFound(SeriesNotFoundWithSeriesID);

            series.DeleteSeries(deleteFiles, true);

            return Ok();
        }

        /// <summary>
        /// Get all relations to series available in the local database for series with ID
        /// </summary>
        /// <param name="seriesID">Shoko ID</param>
        /// <returns></returns>
        [HttpGet("{seriesID}/Relations")]
        public ActionResult<List<SeriesRelation>> GetShokoRelationsBySeriesID([FromRoute] int seriesID)
        {
            var series = RepoFactory.AnimeSeries.GetByID(seriesID);
            if (series == null)
                return NotFound(SeriesNotFoundWithSeriesID);
            if (!User.AllowedSeries(series))
                return Forbid(SeriesForbiddenForUser);
            
            // TODO: Replace with a more generic implementation capable of suplying relations from more than just AniDB.
            var seriesRelations = RepoFactory.AniDB_Anime_Relation.GetByAnimeID(series.AniDB_ID);
            return seriesRelations
                .Select(relation => KeyValuePair.Create(relation, RepoFactory.AnimeSeries.GetByAnimeID(relation.RelatedAnimeID)))
                .Where(tuple => tuple.Value != null)
                .Select(tuple => new SeriesRelation(HttpContext, tuple.Key, series, tuple.Value))
                .ToList();
        }
        
        #endregion
        #region AniDB

        /// <summary>
        /// Get AniDB Info for series with ID
        /// </summary>
        /// <param name="seriesID">Shoko ID</param>
        /// <returns></returns>
        [HttpGet("{seriesID}/AniDB")]
        public ActionResult<Series.AniDB> GetSeriesAnidbBySeriesID([FromRoute] int seriesID)
        {
            var series = RepoFactory.AnimeSeries.GetByID(seriesID);
            if (series == null)
                return NotFound(SeriesNotFoundWithSeriesID);
            if (!User.AllowedSeries(series))
                return Forbid(SeriesForbiddenForUser);

            var anidb = series.GetAnime();
            if (anidb == null)
                return InternalError(AnidbNotFoundForSeriesID);

            return new Series.AniDB(HttpContext, anidb);
        }

        /// <summary>
        /// Get all AniDB relations for the <paramref name="seriesID"/>.
        /// </summary>
        /// <param name="seriesID">Shoko ID</param>
        /// <returns></returns>
        [HttpGet("{seriesID}/AniDB/Relations")]
        public ActionResult<List<SeriesRelation>> GetAnidbRelationsBySeriesID([FromRoute] int seriesID)
        {
            var series = RepoFactory.AnimeSeries.GetByID(seriesID);
            if (series == null)
                return NotFound(SeriesNotFoundWithSeriesID);
            if (!User.AllowedSeries(series))
                return Forbid(SeriesForbiddenForUser);

            var anidb = series.GetAnime();
            if (anidb == null)
                return InternalError(AnidbNotFoundForSeriesID);

            var anidbRelations = RepoFactory.AniDB_Anime_Relation.GetByAnimeID(anidb.AnimeID);
            return anidbRelations
                .Select(relation => new SeriesRelation(HttpContext, relation))
                .ToList();
        }

        #region Recommended For You

        /// <summary>
        /// Gets anidb recommendation for the user.
        /// </summary>
        /// <param name="pageSize">Limits the number of results per page. Set to 0 to disable the limit.</param>
        /// <param name="page">Page number.</param>
        /// <param name="showAll">If enabled will show recommendations across all the anidb available in Shoko, if disabled will only show for the user's collection.</param>
        /// <param name="startDate">Start date to use if recommending for a watch period. Only setting the <paramref name="startDate"/> and not <paramref name="endDate"/> will result in using the watch history from the start date to the present date.</param>
        /// <param name="endDate">End date to use if recommending for a watch period.</param>
        /// <param name="approval">Minumum approval percentage for similar animes.</param>
        /// <param name="recommendationType">Recommendation type for user reviews.</param>
        /// <returns></returns>
        [HttpGet("AniDB/RecommendedForYou")]
        public ActionResult<List<Series.AniDBRecommendedForYou>> GetAnimeRecommendedForYou(
            [FromQuery] [Range(0, 100)] int pageSize = 30,
            [FromQuery] [Range(0, int.MaxValue)] int page = 0,
            [FromQuery] bool showAll = false,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            [FromQuery] double? approval = null,
            [FromQuery] Series.AniDBRecommendationType? recommendationType = null
        )
        {
            if (approval.HasValue && (approval <= 0 || approval > 1))
                approval = null;
            if (startDate.HasValue && !endDate.HasValue)
                endDate = DateTime.Now;
            if (endDate.HasValue && !startDate.HasValue)
                return BadRequest("Missing start date.");
            if (startDate.HasValue && endDate.HasValue)
            {
                if (endDate.Value > DateTime.Now)
                    return BadRequest("End date cannot be set into the future.");
                if (startDate.Value > endDate.Value)
                    return BadRequest("Start date cannot be newer than the end date.");
            }

            var user = HttpContext.GetUser();
            var watchedAnimeList =  GetWatchedAnimeForPeriod(user, startDate, endDate);
            var unwatchedAnimeDict = GetUnwatchedAnime(user, showAll, !startDate.HasValue && !endDate.HasValue ? watchedAnimeList : null);
            var recommendations = watchedAnimeList
                .SelectMany(anime => 
                {
                    if (approval.HasValue)
                        return anime.GetSimilarAnime().Where(similar => unwatchedAnimeDict.Keys.Contains(similar.SimilarAnimeID) && ((double)(similar.Approval / similar.Total) >= approval.Value));
                    return anime.GetSimilarAnime().Where(similar => unwatchedAnimeDict.Keys.Contains(similar.SimilarAnimeID));
                })
                .GroupBy(anime => anime.SimilarAnimeID)
                .Select(similarTo =>
                {
                    var anime = unwatchedAnimeDict[similarTo.Key];
                    var recommendations = anime.GetRecommendations();
                    if (recommendationType.HasValue)
                        recommendations = recommendationType.Value == Series.AniDBRecommendationType.None ? new() : recommendations.Where(rec => rec.GetRecommendationTypeEnum() == (AniDBRecommendationType)recommendationType.Value).ToList();
                    return new Series.AniDBRecommendedForYou()
                    {
                        Anime = new Series.AniDB(HttpContext, anime),
                        SimilarTo = similarTo.Count(),
                        UserRecommendations = recommendations.Count(),
                    };
                })
                .OrderByDescending(e => e.SimilarTo * e.UserRecommendations);

            if (pageSize <= 0)
                return recommendations
                    .ToList();
            return recommendations
                .Skip(pageSize * page)
                .Take(pageSize)
                .ToList();
        }

        /// <summary>
        /// Get all watched anime in a given period of time for the <paramref name="user"/>.
        /// If the <paramref name="startDate"/> and <paramref name="endDate"/>
        /// is omitted then it will return all watched anime for the <paramref name="user"/>.
        /// </summary>
        /// <param name="user">The user to get the watched anime for.</param>
        /// <param name="startDate">The start date of the period.</param>
        /// <param name="endDate">The end date of the period.</param>
        /// <returns>The watched anime for the user.</returns>
        [NonAction]
        private List<SVR_AniDB_Anime> GetWatchedAnimeForPeriod(SVR_JMMUser user, DateTime? startDate = null, DateTime? endDate = null)
        {
            var userDataQuery = RepoFactory.VideoLocalUser.Cache.Values
                .Where(userData => userData.JMMUserID == user.JMMUserID && userData.WatchedDate.HasValue);
            if (startDate.HasValue && endDate.HasValue)
                userDataQuery = userDataQuery.Where(userData => userData.WatchedDate.Value >= startDate.Value && userData.WatchedDate.Value <= endDate.Value);
            return userDataQuery
                .OrderByDescending(userData => userData.LastUpdated)
                .Select(userData => RepoFactory.VideoLocal.GetByID(userData.VideoLocalID))
                .Where(file => file != null)
                .Select(file => file.EpisodeCrossRefs.OrderBy(xref => xref.EpisodeOrder).ThenBy(xref => xref.Percentage).FirstOrDefault())
                .Select(xref => RepoFactory.AnimeEpisode.GetByAniDBEpisodeID(xref.EpisodeID))
                .Where(episode => episode != null)
                .DistinctBy(episode => episode.AnimeSeriesID)
                .Select(episode => episode.GetAnimeSeries().GetAnime())
                .Where(anime => user.AllowedAnime(anime))
                .ToList();
        }

        /// <summary>
        /// Get all unwatched anime for the user.
        /// </summary>
        /// <param name="user">The user to get the unwatched anime for.</param>
        /// <param name="showAll">If true will get a list of all available anime in shoko, regardless of if it's part of the user's collection or not.</param>
        /// <param name="watchedAnime">Optional. Re-use an existing list of the watched anime.</param>
        /// <returns>The unwatched anime for the user.</returns>
        [NonAction]
        private Dictionary<int, SVR_AniDB_Anime> GetUnwatchedAnime(SVR_JMMUser user, bool showAll, IEnumerable<SVR_AniDB_Anime> watchedAnime = null)
        {
            // Get all watched series (reuse if date is not set)
            var watchedSeriesSet = (watchedAnime ?? GetWatchedAnimeForPeriod(user))
                .Select(series => series.AnimeID)
                .ToHashSet();

            if (showAll)
                return RepoFactory.AniDB_Anime.Cache.Values
                    .Where(anime => user.AllowedAnime(anime) && !watchedSeriesSet.Contains(anime.AnimeID))
                    .ToDictionary(anime => anime.AnimeID);

            return RepoFactory.AnimeSeries.Cache.Values
                .Where(series => user.AllowedSeries(series) && !watchedSeriesSet.Contains(series.AniDB_ID))
                .ToDictionary(series => series.AniDB_ID, series => series.GetAnime());
        }

        #endregion

        /// <summary>
        /// Get AniDB Info from the AniDB ID
        /// </summary>
        /// <param name="anidbID">AniDB ID</param>
        /// <returns></returns>
        [HttpGet("AniDB/{anidbID}")]
        public ActionResult<Series.AniDB> GetSeriesAnidbByAnidbID([FromRoute] int anidbID)
        {
            var anidb = RepoFactory.AniDB_Anime.GetByAnimeID(anidbID);
            if (anidb == null)
                return NotFound(AnidbNotFoundForAnidbID);
            if (!User.AllowedAnime(anidb))
                return Forbid(AnidbForbiddenForUser);

            return new Series.AniDB(HttpContext, anidb);
        }

        /// <summary>
        /// Get all anidb relations for the <paramref name="anidbID"/>.
        /// </summary>
        /// <param name="anidbID">AniDB ID</param>
        /// <returns></returns>
        [HttpGet("AniDB/{anidbID}/Relations")]
        public ActionResult<List<SeriesRelation>> GetAnidbRelationsByAnidbID([FromRoute] int anidbID)
        {
            var anidb = RepoFactory.AniDB_Anime.GetByAnimeID(anidbID);
            if (anidb == null)
                return NotFound(AnidbNotFoundForAnidbID);
            if (!User.AllowedAnime(anidb))
                return Forbid(AnidbForbiddenForUser);

            return RepoFactory.AniDB_Anime_Relation.GetByAnimeID(anidbID)
                .Select(relation => new SeriesRelation(HttpContext, relation))
                .ToList();
        }

        /// <summary>
        /// Get a Series from the AniDB ID
        /// </summary>
        /// <param name="anidbID">AniDB ID</param>
        /// <returns></returns>
        [HttpGet("AniDB/{anidbID}/Series")]
        public ActionResult<Series> GetSeriesByAnidbID([FromRoute] int anidbID)
        {
            var series = RepoFactory.AnimeSeries.GetByAnimeID(anidbID);
            if (series == null)
                return NotFound(SeriesNotFoundWithAnidbID);
            if (!User.AllowedSeries(series))
                return Forbid(SeriesForbiddenForUser);

            return new Series(HttpContext, series);
        }

        /// <summary>
        /// Queue a refresh of the AniDB Info for series with AniDB ID
        /// </summary>
        /// <param name="anidbID">AniDB ID</param>
        /// <param name="force">Forcefully retrive updated data from AniDB</param>
        /// <param name="downloadRelations">Download relations for the series</param>
        /// <param name="createSeriesEntry">Also create the Series entries if they doesn't exist</param>
        /// <param name="immediate">Try to immediately refresh the data if we're not HTTP banned.</param>
        /// <returns>True if the refresh is done, otherwise false if it was queued.</returns>
        [HttpPost("AniDB/{anidbID}/Refresh")]
        public ActionResult<bool> RefreshAnidbByAnidbID([FromRoute] int anidbID, [FromQuery] bool force = false, [FromQuery] bool downloadRelations = false, [FromQuery] bool? createSeriesEntry = null, [FromQuery] bool immediate = false)
        {
            if (!createSeriesEntry.HasValue)
                createSeriesEntry = ServerSettings.Instance.AniDb.AutomaticallyImportSeries;

            return Series.QueueAniDBRefresh(anidbID, force, downloadRelations, createSeriesEntry.Value, immediate);
        }

        /// <summary>
        /// Queue a refresh of the AniDB Info for series with ID
        /// </summary>
        /// <param name="seriesID">Shoko ID</param>
        /// <param name="force">Forcefully retrive updated data from AniDB</param>
        /// <param name="downloadRelations">Download relations for the series</param>
        /// <param name="createSeriesEntry">Create the Series entries for related series if they doesn't exist</param>
        /// <param name="immediate">Try to immediately refresh the data if we're not HTTP banned.</param>
        /// <returns>True if the refresh is done, otherwise false if it was queued.</returns>
        [HttpPost("{seriesID}/AniDB/Refresh")]
        public ActionResult<bool> RefreshAnidbBySeriesID([FromRoute] int seriesID, [FromQuery] bool force = false, [FromQuery] bool downloadRelations = false, [FromQuery] bool? createSeriesEntry = null, [FromQuery] bool immediate = false)
        {
            if (!createSeriesEntry.HasValue)
                createSeriesEntry = ServerSettings.Instance.AniDb.AutomaticallyImportSeries;

            var series = RepoFactory.AnimeSeries.GetByID(seriesID);
            if (series == null)
                return NotFound(SeriesNotFoundWithSeriesID);
            if (!User.AllowedSeries(series))
                return Forbid(SeriesForbiddenForUser);

            var anidb = series.GetAnime();
            if (anidb == null)
                return InternalError(AnidbNotFoundForSeriesID);

            return Series.QueueAniDBRefresh(anidb.AnimeID, force, downloadRelations, createSeriesEntry.Value, immediate);
        }

        /// <summary>
        /// Forcefully refresh the AniDB Info from XML on disk for series with ID
        /// </summary>
        /// <param name="seriesID">Shoko ID</param>
        /// <returns>True if the refresh is done, otherwise false if it failed.</returns>
        [HttpPost("{seriesID}/AniDB/Refresh/ForceFromXML")]
        public ActionResult<bool> RefreshAnidbFromXML([FromRoute] int seriesID)
        {
            var series = RepoFactory.AnimeSeries.GetByID(seriesID);
            if (series == null)
                return NotFound(SeriesNotFoundWithSeriesID);
            if (!User.AllowedSeries(series))
                return Forbid(SeriesForbiddenForUser);

            var anime = series.GetAnime();
            if (anime == null)
                return InternalError(AnidbNotFoundForSeriesID);

            return Series.RefreshAniDBFromCachedXML(HttpContext, anime);
        }
    
        #endregion
        #region TvDB

        /// <summary>
        /// Get TvDB Info for series with ID
        /// </summary>
        /// <param name="seriesID">Shoko ID</param>
        /// <returns></returns>
        [HttpGet("{seriesID}/TvDB")]
        public ActionResult<List<Series.TvDB>> GetSeriesTvdb([FromRoute] int seriesID)
        {
            var series = RepoFactory.AnimeSeries.GetByID(seriesID);
            if (series == null)
                return NotFound(TvdbNotFoundForSeriesID);
            if (!User.AllowedSeries(series))
                return Forbid(TvdbForbiddenForUser);

            return Series.GetTvDBInfo(HttpContext, series);
        }

        /// <summary>
        /// Get TvDB Info from the TvDB ID
        /// </summary>
        /// <param name="tvdbID">TvDB ID</param>
        /// <returns></returns>
        [HttpGet("TvDB/{tvdbID}")]
        public ActionResult<Series.TvDB> GetSeriesTvdbByTvdbID([FromRoute] int tvdbID)
        {
            var tvdb = RepoFactory.TvDB_Series.GetByTvDBID(tvdbID);
            if (tvdb == null)
                return NotFound(TvdbNotFoundForTvdbID);
            var xref = RepoFactory.CrossRef_AniDB_TvDB.GetByTvDBID(tvdbID).FirstOrDefault();
            if (xref == null)
                return NotFound(TvdbNotFoundForTvdbID);
            var series = RepoFactory.AnimeSeries.GetByAnimeID(xref.AniDBID);
            if (series == null)
                return NotFound(TvdbNotFoundForTvdbID);

            if (!User.AllowedSeries(series))
                return Forbid(TvdbForbiddenForUser);

            return new Series.TvDB(HttpContext, tvdb, series, series.GetAnimeEpisodes());
        }

        /// <summary>
        /// Get a Series from the TvDB ID
        /// </summary>
        /// <param name="tvdbID">TvDB ID</param>
        /// <returns></returns>
        [HttpGet("TvDB/{tvdbID}/Series")]
        public ActionResult<List<Series>> GetSeriesByTvdbID([FromRoute] int tvdbID)
        {
            var tvdb = RepoFactory.TvDB_Series.GetByTvDBID(tvdbID);
            if (tvdb == null)
                return NotFound(TvdbNotFoundForTvdbID);
            var seriesList = RepoFactory.CrossRef_AniDB_TvDB.GetByTvDBID(tvdbID)
                .Select(xref => RepoFactory.AnimeSeries.GetByAnimeID(xref.AniDBID))
                .ToList();

            if (seriesList.Any(series => !User.AllowedSeries(series)))
                return Forbid(SeriesForbiddenForUser);

            return seriesList
                .Select(series => new Series(HttpContext, series))
                .ToList();
        }

        #endregion
        #endregion
        #region Vote

        /// <summary>
        /// Add a permanent or temprary user-submitted rating for the series.
        /// </summary>
        /// <param name="seriesID"></param>
        /// <param name="vote"></param>
        /// <returns></returns>
        [HttpPost("{seriesID}/Vote")]
        public ActionResult PostSeriesUserVote([FromRoute] int seriesID, [FromBody] Vote vote)
        {
            var series = RepoFactory.AnimeSeries.GetByID(seriesID);
            if (series == null)
                return NotFound(SeriesNotFoundWithSeriesID);
            if (!User.AllowedSeries(series))
                return Forbid(SeriesForbiddenForUser);

            if (vote.Value < 0)
                return BadRequest("Value must be greater than or equal to 0.");
            if (vote.Value > vote.MaxValue)
                return BadRequest($"Value must be less than or equal to the set max value ({vote.MaxValue}).");
            if (vote.MaxValue <= 0)
                return BadRequest("Max value must be an integer above 0.");

            Series.AddSeriesVote(HttpContext, series, User.JMMUserID, vote);

            return NoContent();
        }
        
        #endregion
        #region Images
        #region All images

        private static HashSet<Image.ImageType> AllowedImageTypes = new() { Image.ImageType.Poster, Image.ImageType.Banner, Image.ImageType.Fanart };

        private static string InvalidIDForSource = "Invalid image id for selected source.";

        private static string InvalidImageTypeForSeries = "Invalid image type for series images.";

        private static string InvalidImageIsDisabled = "Image is disabled.";

        /// <summary>
        /// Get all images for series with ID, optionally with Disabled images, as well.
        /// </summary>
        /// <param name="seriesID">Shoko ID</param>
        /// <param name="includeDisabled"></param>
        /// <returns></returns>
        [HttpGet("{seriesID}/Images")]
        public ActionResult<Images> GetSeriesImages([FromRoute] int seriesID, [FromQuery] bool includeDisabled)
        {
            var series = RepoFactory.AnimeSeries.GetByID(seriesID);
            if (series == null)
                return NotFound(SeriesNotFoundWithSeriesID);
            if (!User.AllowedSeries(series))
                return Forbid(SeriesForbiddenForUser);

            return Series.GetArt(HttpContext, series.AniDB_ID, includeDisabled);
        }

        #endregion
        #region Default image

        /// <summary>
        /// Get the default <see cref="Image"/> for the given <paramref name="imageType"/> for the <see cref="Series"/>.
        /// </summary>
        /// <param name="seriesID">Series ID</param>
        /// <param name="imageType">Poster, Banner, Fanart</param>
        /// <returns></returns>
        [HttpGet("{seriesID}/Images/{imageType}")]
        public ActionResult<Image> GetSeriesDefaultImageForType([FromRoute] int seriesID, [FromRoute] Image.ImageType imageType)
        {
            var series = RepoFactory.AnimeSeries.GetByID(seriesID);
            if (series == null)
                return NotFound(SeriesNotFoundWithSeriesID);
            if (!User.AllowedSeries(series))
                return Forbid(SeriesForbiddenForUser);

            if (!AllowedImageTypes.Contains(imageType))
                return BadRequest(InvalidImageTypeForSeries);

            var imageSizeType = Image.GetImageSizeTypeFromType(imageType);
            var defaultBanner = Series.GetDefaultImage(series.AniDB_ID, imageSizeType);
            if (defaultBanner != null)
                return defaultBanner;

            var images = Series.GetArt(HttpContext, series.AniDB_ID);
            return imageSizeType switch
            {
                ImageSizeType.Poster => images.Posters.FirstOrDefault(),
                ImageSizeType.WideBanner => images.Banners.FirstOrDefault(),
                ImageSizeType.Fanart => images.Fanarts.FirstOrDefault(),
                _ => null,
            };
        }


        /// <summary>
        /// Set the default <see cref="Image"/> for the given <paramref name="imageType"/> for the <see cref="Series"/>.
        /// </summary>
        /// <param name="seriesID">Series ID</param>
        /// <param name="imageType">Poster, Banner, Fanart</param>
        /// <param name="body">The body containing the source and id used to set.</param>
        /// <returns></returns>
        [HttpPatch("{seriesID}/Images/{imageType}")]
        public ActionResult<Image> SetSeriesDefaultImageForType([FromRoute] int seriesID, [FromRoute] Image.ImageType imageType, [FromBody] Image.Input.DefaultImageBody body)
        {
            var series = RepoFactory.AnimeSeries.GetByID(seriesID);
            if (series == null)
                return NotFound(SeriesNotFoundWithSeriesID);
            if (!User.AllowedSeries(series))
                return Forbid(SeriesForbiddenForUser);

            if (!AllowedImageTypes.Contains(imageType))
                return BadRequest(InvalidImageTypeForSeries);

            var imageEntityType = Image.GetImageTypeFromSourceAndType(body.Source, imageType);
            if (!imageEntityType.HasValue)
                return BadRequest("Invalid body source");

            // All dynamic ids are stringified ints, so extract the image id from the body.
            if (!int.TryParse(body.ID, out var imageID))
                return BadRequest("Invalid body id. Id must be a stringified int.");

            // Check if the id is valid for the given type and source.

            switch (imageEntityType.Value)
            {
                // Posters
                case ImageEntityType.AniDB_Cover:
                    if (imageID != series.AniDB_ID)
                        return BadRequest(InvalidIDForSource);
                    break;
                case ImageEntityType.TvDB_Cover:
                    {
                        var tvdbPoster = RepoFactory.TvDB_ImagePoster.GetByID(imageID);
                        if (tvdbPoster == null)
                            return BadRequest(InvalidIDForSource);
                        if (tvdbPoster.Enabled != 1)
                            return BadRequest(InvalidImageIsDisabled);
                        break;
                    }
                case ImageEntityType.MovieDB_Poster:
                    var tmdbPoster = RepoFactory.MovieDB_Poster.GetByID(imageID);
                    if (tmdbPoster == null)
                        return BadRequest(InvalidIDForSource);
                    if (tmdbPoster.Enabled != 1)
                        return BadRequest(InvalidImageIsDisabled);
                    break;

                // Banners
                case ImageEntityType.TvDB_Banner:
                    var tvdbBanner = RepoFactory.TvDB_ImageWideBanner.GetByID(imageID);
                    if (tvdbBanner == null)
                        return BadRequest(InvalidIDForSource);
                    if (tvdbBanner.Enabled != 1)
                        return BadRequest(InvalidImageIsDisabled);
                    break;

                // Fanart
                case ImageEntityType.TvDB_FanArt:
                    var tvdbFanart = RepoFactory.TvDB_ImageFanart.GetByID(imageID);
                    if (tvdbFanart == null)
                        return BadRequest(InvalidIDForSource);
                    if (tvdbFanart.Enabled != 1)
                        return BadRequest(InvalidImageIsDisabled);
                    break;
                case ImageEntityType.MovieDB_FanArt:
                    var tmdbFanart = RepoFactory.MovieDB_Fanart.GetByID(imageID);
                    if (tmdbFanart == null)
                        return BadRequest(InvalidIDForSource);
                    if (tmdbFanart.Enabled != 1)
                        return BadRequest(InvalidImageIsDisabled);
                    break;

                // Not allowed.
                default:
                    return BadRequest("Invalid source and/or type");
            }

            var imageSizeType = Image.GetImageSizeTypeFromType(imageType);
            var defaultImage = RepoFactory.AniDB_Anime_DefaultImage.GetByAnimeIDAndImagezSizeType(series.AniDB_ID, imageSizeType) ?? new() { AnimeID = series.AniDB_ID, ImageType = (int)imageSizeType };
            defaultImage.ImageParentID = imageID;
            defaultImage.ImageParentType = (int)imageEntityType.Value;

            // Create or update the entry.
            RepoFactory.AniDB_Anime_DefaultImage.Save(defaultImage);

            // Update the contract data (used by Shoko Desktop).
            RepoFactory.AnimeSeries.Save(series, false);

            return new Image(imageID, imageEntityType.Value, true);
        }

        /// <summary>
        /// Unset the default <see cref="Image"/> for the given <paramref name="imageType"/> for the <see cref="Series"/>.
        /// </summary>
        /// <param name="seriesID"></param>
        /// <param name="imageType">Poster, Banner, Fanart</param>
        /// <returns></returns>
        [HttpDelete("{seriesID}/Images/{imageType}")]
        public ActionResult DeleteSeriesDefaultImageForType([FromRoute] int seriesID, [FromRoute] Image.ImageType imageType)
        {
            // Check if the series exists and if the user can access the series.
            var series = RepoFactory.AnimeSeries.GetByID(seriesID);
            if (series == null)
                return NotFound(SeriesNotFoundWithSeriesID);
            if (!User.AllowedSeries(series))
                return Forbid(SeriesForbiddenForUser);

            // Check if
            if (!AllowedImageTypes.Contains(imageType))
                return BadRequest(InvalidImageTypeForSeries);

            // Check if a default image is set.
            var imageSizeType = Image.GetImageSizeTypeFromType(imageType);
            var defaultImage = RepoFactory.AniDB_Anime_DefaultImage.GetByAnimeIDAndImagezSizeType(series.AniDB_ID, imageSizeType);
            if (defaultImage == null)
                return BadRequest("No default banner.");

            // Delete the entry.
            RepoFactory.AniDB_Anime_DefaultImage.Delete(defaultImage);

            // Update the contract data (used by Shoko Desktop).
            RepoFactory.AnimeSeries.Save(series, false);

            // Don't return any content.
            return NoContent();
        }
        
        #endregion
        #endregion
        #region Tags

        /// <summary>
        /// Get tags for Series with ID, optionally applying the given <see cref="TagFilter.Filter" />
        /// </summary>
        /// <param name="seriesID">Shoko ID</param>
        /// <param name="filter"></param>
        /// <param name="excludeDescriptions"></param>
        /// <returns></returns>
        [HttpGet("{seriesID}/Tags")]
        public ActionResult<List<Tag>> GetSeriesTags([FromRoute] int seriesID, [FromQuery] TagFilter.Filter filter = 0, [FromQuery] bool excludeDescriptions = false)
        {
            var series = RepoFactory.AnimeSeries.GetByID(seriesID);
            if (series == null)
                return NotFound(SeriesNotFoundWithSeriesID);
            if (!User.AllowedSeries(series))
                return Forbid(SeriesForbiddenForUser);

            var anidb = series.GetAnime();
            if (anidb == null) return new List<Tag>();

            return Series.GetTags(HttpContext, anidb, filter, excludeDescriptions);
        }

        /// <summary>
        /// Get tags for Series with ID, applying the given TagFilter (0 is show all)
        /// </summary>
        ///
        /// <param name="seriesID">Shoko ID</param>
        /// <param name="filter"></param>
        /// <param name="excludeDescriptions"></param>
        /// <returns></returns>
        [HttpGet("{seriesID}/Tags/{filter}")]
        [Obsolete]
        public ActionResult<List<Tag>> GetSeriesTagsFromPath([FromRoute] int seriesID, [FromRoute] TagFilter.Filter filter, [FromQuery] bool excludeDescriptions = false)
            => GetSeriesTags(seriesID, filter, excludeDescriptions);

        #endregion
        #region Cast

        /// <summary>
        /// Get the cast listing for series with ID
        /// </summary>
        /// <param name="seriesID">Shoko ID</param>
        /// <param name="roleType">Filter by role type</param>
        /// <returns></returns>
        [HttpGet("{seriesID}/Cast")]
        public ActionResult<List<Role>> GetSeriesCast([FromRoute] int seriesID, [FromQuery] Role.CreatorRoleType? roleType = null)
        {
            var series = RepoFactory.AnimeSeries.GetByID(seriesID);
            if (series == null)
                return NotFound(SeriesNotFoundWithSeriesID);
            if (!User.AllowedSeries(series))
                return Forbid(SeriesForbiddenForUser);

            return Series.GetCast(HttpContext, series.AniDB_ID, roleType);
        }

        #endregion
        #region Group
        
        /// <summary>
        /// Move the series to a new group, and update accordingly
        /// </summary>
        /// <param name="seriesID">Shoko ID</param>
        /// <param name="groupID"></param>
        /// <returns></returns>
        [Authorize("admin")]
        [HttpPatch("{seriesID}/Move/{groupID}")]
        public ActionResult MoveSeries([FromRoute] int seriesID, [FromRoute] int groupID)
        {
            var series = RepoFactory.AnimeSeries.GetByID(seriesID);
            if (series == null)
                return NotFound(SeriesNotFoundWithSeriesID);
            if (!User.AllowedSeries(series))
                return Forbid(SeriesForbiddenForUser);

            var group = RepoFactory.AnimeGroup.GetByID(groupID);
            if (group == null)
                return BadRequest("No Group entry for the given groupID");

            series.MoveSeries(group);

            return Ok();
        }

        #endregion
        #region Search

        /// <summary>
        /// Search for series with given query in name or tag
        /// </summary>
        /// <param name="query">target string</param>
        /// <param name="fuzzy">whether or not to use fuzzy search</param>
        /// <param name="limit">number of return items</param>
        /// <returns>List<see cref="SeriesSearchResult"/></returns>
        [HttpGet("Search/{query}")]
        public ActionResult<IEnumerable<SeriesSearchResult>> Search([FromRoute] string query, [FromQuery] bool fuzzy = true, [FromQuery] int limit = int.MaxValue)
        {
            SorensenDice search = new SorensenDice();
            query = query.ToLowerInvariant();
            query = query.Replace("+", " ");

            List<SeriesSearchResult> seriesList = new List<SeriesSearchResult>();
            ParallelQuery<SVR_AnimeSeries> allSeries = RepoFactory.AnimeSeries.GetAll()
                .Where(a => a?.Contract?.AniDBAnime?.AniDBAnime != null &&
                            !a.Contract.AniDBAnime.Tags.Select(b => b.TagName)
                                .FindInEnumerable(User.GetHideCategories()))
                .AsParallel();

            HashSet<string> languages = new HashSet<string>{"en", "x-jat"};
            languages.UnionWith(ServerSettings.Instance.LanguagePreference);
            var distLevenshtein = new ConcurrentDictionary<SVR_AnimeSeries, Tuple<double, string>>();

            if (fuzzy)
                allSeries.ForAll(a => CheckTitlesFuzzy(search, languages, a, query, ref distLevenshtein, limit));
            else
                allSeries.ForAll(a => CheckTitles(languages, a, query, ref distLevenshtein, limit));

            var tempListToSort = distLevenshtein.Keys.GroupBy(a => a.AnimeGroupID).Select(a =>
            {
                var tempSeries = a.ToList();
                tempSeries.Sort((j, k) =>
                {
                    var result1 = distLevenshtein[j];
                    var result2 = distLevenshtein[k];
                    var exactMatch = result1.Item1.CompareTo(result2.Item1);
                    if (exactMatch != 0) return exactMatch;

                    string title1 = j.GetSeriesName();
                    string title2 = k.GetSeriesName();
                    if (title1 == null && title2 == null) return 0;
                    if (title1 == null) return 1;
                    if (title2 == null) return -1;
                    return string.Compare(title1, title2, StringComparison.InvariantCultureIgnoreCase);
                });
                var result = new SearchGrouping
                {
                    Series = a.OrderBy(b => b.AirDate).ToList(),
                    Distance = distLevenshtein[tempSeries[0]].Item1,
                    Match = distLevenshtein[tempSeries[0]].Item2
                };
                return result;
            });

            Dictionary<SVR_AnimeSeries, Tuple<double, string>> series = tempListToSort.OrderBy(a => a.Distance)
                .ThenBy(a => a.Match.Length).SelectMany(a => a.Series).ToDictionary(a => a, a => distLevenshtein[a]);
            foreach (KeyValuePair<SVR_AnimeSeries, Tuple<double, string>> ser in series)
            {
                seriesList.Add(new SeriesSearchResult(HttpContext, ser.Key, ser.Value.Item2, ser.Value.Item1));
                if (seriesList.Count >= limit)
                {
                    break;
                }
            }

            return seriesList;
        }

        /// <summary>
        /// Search the title dump for the given query or directly using the anidb id.
        /// </summary>
        /// <param name="query">target string</param>
        /// <param name="includeTitles">Include titles</param>
        /// <returns></returns>
        [HttpGet("AniDB/Search/{query}")]
        public ActionResult<List<Series.AniDBSearchResult>> OnlineAnimeTitleSearch([FromRoute] string query, [FromQuery] bool includeTitles = true)
        {
            List<Series.AniDBSearchResult> list = new List<Series.AniDBSearchResult>();

            // check if it is a title search or an ID search
            if (int.TryParse(query, out int aid))
            {
                // user is direct entering the anime id

                // try the local database first
                // if not download the data from AniDB now
                SVR_AniDB_Anime anime = Server.ShokoService.AnidbProcessor.GetAnimeInfoHTTP(aid, false,
                    ServerSettings.Instance.AniDb.DownloadRelatedAnime);
                if (anime != null)
                {
                    Series.AniDBSearchResult res = new Series.AniDBSearchResult
                    {
                        ID = anime.AnimeID,
                        Title = anime.MainTitle,
                        Titles = includeTitles ? anime.GetTitles().Select(title => new Title
                            {
                                Language = title.Language,
                                Name = title.Title,
                                Type = title.TitleType,
                                Default = string.Equals(title.Title, anime.MainTitle),
                                Source = "AniDB",
                            }
                        ).ToList() : null,
                    };

                    // check for existing series and group details
                    SVR_AnimeSeries ser = RepoFactory.AnimeSeries.GetByAnimeID(anime.AnimeID);
                    if (ser != null)
                    {
                        res.ShokoID = ser.AnimeSeriesID;
                        res.Title = anime.GetFormattedTitle();
                    }
                    list.Add(res);
                }
            }
            else
            {
                // title search so look at the web cache
                var result = AniDB_TitleHelper.Instance.SearchTitle(HttpUtility.UrlDecode(query));

                foreach (var item in result)
                {
                    var mainTitle = (item.Titles.FirstOrDefault(a => a.TitleLanguage == "x-jat" && a.TitleType == "main") ?? item.Titles.FirstOrDefault());
                    Series.AniDBSearchResult res = new Series.AniDBSearchResult
                    {
                        ID = item.AnimeID,
                        Title = mainTitle.Title,
                        Titles = includeTitles ? item.Titles.Select(title => new Title
                            {
                                Language = title.TitleLanguage,
                                Name = title.Title,
                                Type = title.TitleType,
                                Default = title == mainTitle,
                                Source = "AniDB",
                            }
                        ).ToList() : null,
                    };

                    // check for existing series and group details
                    SVR_AnimeSeries ser = RepoFactory.AnimeSeries.GetByAnimeID(item.AnimeID);
                    if (ser != null)
                    {
                        res.ShokoID = ser.AnimeSeriesID;
                        res.Title = ser.GetAnime().GetFormattedTitle();
                    }

                    list.Add(res);
                }
            }

            return list;
        }

        /// <summary>
        /// Searches for series whose title starts with a string
        /// </summary>
        /// <param name="query"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        [HttpGet("StartsWith/{query}")]
        public ActionResult<List<SeriesSearchResult>> StartsWith([FromRoute] string query, [FromQuery] int limit = int.MaxValue)
        {
            query = query.ToLowerInvariant();

            List<SeriesSearchResult> seriesList = new List<SeriesSearchResult>();
            ConcurrentDictionary<SVR_AnimeSeries, string> tempSeries = new ConcurrentDictionary<SVR_AnimeSeries, string>();
            ParallelQuery<SVR_AnimeSeries> allSeries = RepoFactory.AnimeSeries.GetAll()
                .Where(a => a?.Contract?.AniDBAnime?.AniDBAnime != null &&
                            !a.Contract.AniDBAnime.Tags.Select(b => b.TagName)
                                .FindInEnumerable(User.GetHideCategories()))
                .AsParallel();

            #region Search_TitlesOnly
            allSeries.ForAll(a => CheckTitlesStartsWith(a, query, ref tempSeries, limit));
            Dictionary<SVR_AnimeSeries, string> series =
                tempSeries.OrderBy(a => a.Value).ToDictionary(a => a.Key, a => a.Value);

            foreach (KeyValuePair<SVR_AnimeSeries, string> ser in series)
            {
                seriesList.Add(new SeriesSearchResult(HttpContext, ser.Key, ser.Value, 0));
                if (seriesList.Count >= limit)
                {
                    break;
                }
            }

            #endregion

            return seriesList;
        }

        /// <summary>
        /// Get the series that reside in the path that ends with <param name="path"></param>
        /// </summary>
        /// <returns></returns>
        [HttpGet("PathEndsWith/{*path}")]
        public ActionResult<List<Series>> GetSeries([FromRoute] string path)
        {
            var query = path;
            if (query.Contains("%") || query.Contains("+")) query = Uri.UnescapeDataString(query);
            if (query.Contains("%")) query = Uri.UnescapeDataString(query);
            query = query.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar);
            // There should be no circumstance where FullServerPath has no Directory Name, unless you have missing import folders
            return RepoFactory.VideoLocalPlace.GetAll().AsParallel()
                .Where(a => a.FullServerPath != null && Path.GetDirectoryName(a.FullServerPath)
                    .EndsWith(query, StringComparison.OrdinalIgnoreCase))
                .SelectMany(a => a.VideoLocal.GetAnimeEpisodes()).Select(a => a.GetAnimeSeries())
                .Distinct()
                .Where(ser => ser == null || User.AllowedSeries(ser)).Select(a => new Series(HttpContext, a)).ToList();
        }

        #region Helpers

        /// <summary>
        /// function used in fuzzy search
        /// </summary>
        /// <param name="search"></param>
        /// <param name="languages"></param>
        /// <param name="a"></param>
        /// <param name="query"></param>
        /// <param name="distLevenshtein"></param>
        /// <param name="limit"></param>
        [NonAction]
        private static void CheckTitlesFuzzy(IStringDistance search, HashSet<string> languages, SVR_AnimeSeries a, string query,
            ref ConcurrentDictionary<SVR_AnimeSeries, Tuple<double, string>> distLevenshtein, int limit)
        {
            if (distLevenshtein.Count >= limit) return;
            if (a?.Contract?.AniDBAnime?.AnimeTitles == null) return;
            var dist = double.MaxValue;
            string match = string.Empty;

            var seriesTitles = a.Contract.AniDBAnime.AnimeTitles
                .Where(b => languages.Contains(b.Language.ToLower()) &&
                            b.TitleType != Shoko.Models.Constants.AnimeTitleType.ShortName).Select(b => b.Title)
                .ToList();
            foreach (string title in seriesTitles)
            {
                if (string.IsNullOrWhiteSpace(title)) continue;
                var result = 0.0;
                // Check for exact match
                if (!title.Equals(query, StringComparison.Ordinal))
                    result = search.Distance(title, query);
                // For Dice, 1 is no reasonable match
                if (result >= 1) continue;
                // Don't count an error as liberally when the title is short
                if (title.Length < 5 && result > 0.8) continue;
                if (result < dist)
                {
                    match = title;
                    dist = result;
                } else if (Math.Abs(result - dist) < 0.00001)
                {
                    if (title.Length < match.Length) match = title;
                }
            }
            // Keep the lowest distance, then by shortest title
            if (dist < double.MaxValue)
                distLevenshtein.AddOrUpdate(a, new Tuple<double, string>(dist, match),
                    (key, oldValue) =>
                    {
                        if (oldValue.Item1 < dist) return oldValue;
                        if (Math.Abs(oldValue.Item1 - dist) < 0.00001)
                            return oldValue.Item2.Length < match.Length
                                ? oldValue
                                : new Tuple<double, string>(dist, match);

                        return new Tuple<double, string>(dist, match);
                    });
        }

        /// <summary>
        /// function used in search
        /// </summary>
        /// <param name="languages"></param>
        /// <param name="a"></param>
        /// <param name="query"></param>
        /// <param name="distLevenshtein"></param>
        /// <param name="limit"></param>
        [NonAction]
        private static void CheckTitles(HashSet<string> languages, SVR_AnimeSeries a, string query,
            ref ConcurrentDictionary<SVR_AnimeSeries, Tuple<double, string>> distLevenshtein, int limit)
        {
            if (distLevenshtein.Count >= limit) return;
            if (a?.Contract?.AniDBAnime?.AnimeTitles == null) return;
            var dist = double.MaxValue;
            string match = string.Empty;

            var seriesTitles = a.Contract.AniDBAnime.AnimeTitles
                .Where(b => languages.Contains(b.Language.ToLower()) &&
                            b.TitleType != Shoko.Models.Constants.AnimeTitleType.ShortName).Select(b => b.Title)
                .ToList();
            foreach (string title in seriesTitles)
            {
                if (string.IsNullOrWhiteSpace(title)) continue;
                var result = 1.0;
                // Check for exact match
                if (title.Equals(query, StringComparison.Ordinal))
                {
                    result = 0.0;
                }
                else
                {
                    var index = title.IndexOf(query, StringComparison.InvariantCultureIgnoreCase);
                    if (index >= 0) result = ((double) title.Length - index) / title.Length * 0.8D; // ensure that 0.8 doesn't skip later
                }
                // For Dice, 1 is no reasonable match
                if (result >= 1) continue;
                // Don't count an error as liberally when the title is short
                if (title.Length < 5 && result > 0.8) continue;
                if (result < dist)
                {
                    match = title;
                    dist = result;
                } else if (Math.Abs(result - dist) < 0.00001)
                {
                    if (title.Length < match.Length) match = title;
                }
            }
            // Keep the lowest distance, then by shortest title
            if (dist < double.MaxValue)
                distLevenshtein.AddOrUpdate(a, new Tuple<double, string>(dist, match),
                    (key, oldValue) =>
                    {
                        if (oldValue.Item1 < dist) return oldValue;
                        if (Math.Abs(oldValue.Item1 - dist) < 0.00001)
                            return oldValue.Item2.Length < match.Length
                                ? oldValue
                                : new Tuple<double, string>(dist, match);

                        return new Tuple<double, string>(dist, match);
                    });
        }

        class SearchGrouping
        {
            public List<SVR_AnimeSeries> Series { get; set; }
            public double Distance { get; set; }
            public string Match { get; set; }
        }

        [NonAction]
        private static void CheckTitlesStartsWith(SVR_AnimeSeries a, string query,
            ref ConcurrentDictionary<SVR_AnimeSeries, string> series, int limit)
        {
            if (series.Count >= limit) return;
            if (a?.Contract?.AniDBAnime?.AniDBAnime.AllTitles == null) return;
            string match = string.Empty;
            foreach (string title in a.Contract.AniDBAnime.AnimeTitles.Select(b => b.Title).ToList())
            {
                if (string.IsNullOrEmpty(title)) continue;
                if (title.StartsWith(query, StringComparison.InvariantCultureIgnoreCase))
                {
                    match = title;
                }
            }
            // Keep the lowest distance
            if (match != string.Empty)
                series.TryAdd(a, match);
        }

        #endregion
        #endregion
    }
}
