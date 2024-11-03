using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FluentNHibernate.Utils;
using Microsoft.Extensions.DependencyInjection;
using NutzCode.InMemoryIndex;
using Quartz;
using Shoko.Commons.Extensions;
using Shoko.Commons.Properties;
using Shoko.Models.Enums;
using Shoko.Models.Server;
using Shoko.Server.Databases;
using Shoko.Server.Exceptions;
using Shoko.Server.Models;
using Shoko.Server.Scheduling;
using Shoko.Server.Scheduling.Jobs.Shoko;
using Shoko.Server.Server;
using Shoko.Server.Services;
using Shoko.Server.Utilities;

#pragma warning disable CS0618
namespace Shoko.Server.Repositories.Cached;

public class VideoLocalRepository : BaseCachedRepository<SVR_VideoLocal, int>
{
    private PocoIndex<int, SVR_VideoLocal, string> _hashes;
    private PocoIndex<int, SVR_VideoLocal, string> _sha1;
    private PocoIndex<int, SVR_VideoLocal, string> _md5;
    private PocoIndex<int, SVR_VideoLocal, string> _crc32;
    private PocoIndex<int, SVR_VideoLocal, bool> _ignored;

    public VideoLocalRepository(DatabaseFactory databaseFactory) : base(databaseFactory)
    {
        DeleteWithOpenTransactionCallback = (ses, obj) =>
        {
            RepoFactory.VideoLocalPlace.DeleteWithOpenTransaction(ses, obj.Places.ToList());
            RepoFactory.VideoLocalUser.DeleteWithOpenTransaction(ses, RepoFactory.VideoLocalUser.GetByVideoLocalID(obj.VideoLocalID));
        };
    }

    protected override int SelectKey(SVR_VideoLocal entity)
    {
        return entity.VideoLocalID;
    }

    public override void PopulateIndexes()
    {
        //Fix null hashes
        foreach (var l in Cache.Values)
        {
            if (l.MD5 != null && l.SHA1 != null && l.Hash != null && l.CRC32 != null && l.FileName != null) continue;

            l.MediaVersion = 0;
            l.MD5 ??= string.Empty;
            l.CRC32 ??= string.Empty;
            l.SHA1 ??= string.Empty;
            l.Hash ??= string.Empty;
            l.FileName ??= string.Empty;
        }

        _hashes = new PocoIndex<int, SVR_VideoLocal, string>(Cache, a => a.Hash);
        _sha1 = new PocoIndex<int, SVR_VideoLocal, string>(Cache, a => a.SHA1);
        _md5 = new PocoIndex<int, SVR_VideoLocal, string>(Cache, a => a.MD5);
        _crc32 = new PocoIndex<int, SVR_VideoLocal, string>(Cache, a => a.CRC32);
        _ignored = new PocoIndex<int, SVR_VideoLocal, bool>(Cache, a => a.IsIgnored);
    }

    public override void RegenerateDb()
    {
        ServerState.Instance.ServerStartingStatus = string.Format(
            Resources.Database_Validating, nameof(VideoLocal), " Checking Media Info"
        );
        var count = 0;
        int max;
        List<SVR_VideoLocal> list;

        try
        {
            list = Cache.Values.Where(a => a.MediaVersion < SVR_VideoLocal.MEDIA_VERSION || a.MediaInfo == null).ToList();
            max = list.Count;

            var scheduler = Utils.ServiceContainer.GetRequiredService<ISchedulerFactory>().GetScheduler().Result;
            list.ForEach(
                a =>
                {
                    scheduler.StartJob<MediaInfoJob>(c => c.VideoLocalID = a.VideoLocalID).GetAwaiter().GetResult();
                    count++;
                    ServerState.Instance.ServerStartingStatus = string.Format(
                        Resources.Database_Validating, nameof(VideoLocal),
                        " Queuing Media Info Commands - " + count + "/" + max
                    );
                }
            );
        }
        catch
        {
            // ignore
        }

        var locals = Cache.Values
            .Where(a => !string.IsNullOrWhiteSpace(a.Hash))
            .GroupBy(a => a.Hash)
            .ToDictionary(g => g.Key, g => g.ToList());
        ServerState.Instance.ServerStartingStatus = string.Format(
            Resources.Database_Validating, nameof(VideoLocal),
            " Cleaning Empty Records"
        );
        using var session = _databaseFactory.SessionFactory.OpenSession();
        using (var transaction = session.BeginTransaction())
        {
            list = Cache.Values.Where(a => a.IsEmpty()).ToList();
            count = 0;
            max = list.Count;
            foreach (var remove in list)
            {
                RepoFactory.VideoLocal.DeleteWithOpenTransaction(session, remove);
                count++;
                ServerState.Instance.ServerStartingStatus = string.Format(
                    Resources.Database_Validating, nameof(VideoLocal),
                    " Cleaning Empty Records - " + count + "/" + max
                );
            }

            transaction.Commit();
        }

        var toRemove = new List<SVR_VideoLocal>();
        var comparer = new VideoLocalComparer();

        ServerState.Instance.ServerStartingStatus = string.Format(
            Resources.Database_Validating, nameof(VideoLocal),
            " Checking for Duplicate Records"
        );

        foreach (var hash in locals.Keys)
        {
            var values = locals[hash];
            values.Sort(comparer);
            var to = values.First();
            var froms = values.Except(to).ToList();
            foreach (var from in froms)
            {
                var places = from.Places;
                if (places == null || places.Count == 0)
                {
                    continue;
                }

                using var transaction = session.BeginTransaction();
                foreach (var place in places)
                {
                    place.VideoLocalID = to.VideoLocalID;
                    RepoFactory.VideoLocalPlace.SaveWithOpenTransaction(session, place);
                }

                transaction.Commit();
            }

            toRemove.AddRange(froms);
        }

        count = 0;
        max = toRemove.Count;
        foreach (var batch in toRemove.Batch(50))
        {
            using var transaction = session.BeginTransaction();
            foreach (var remove in batch)
            {
                count++;
                ServerState.Instance.ServerStartingStatus = string.Format(
                    Resources.Database_Validating, nameof(VideoLocal),
                    " Cleaning Duplicate Records - " + count + "/" + max
                );
                DeleteWithOpenTransaction(session, remove);
            }

            transaction.Commit();
        }
    }

    public List<SVR_VideoLocal> GetByImportFolder(int importFolderID)
    {
        return RepoFactory.VideoLocalPlace.GetByImportFolder(importFolderID)
            .Select(a => GetByID(a.VideoLocalID))
            .Where(a => a != null)
            .Distinct()
            .ToList();
    }

    private void UpdateMediaContracts(SVR_VideoLocal obj)
    {
        if (obj.MediaInfo != null && obj.MediaVersion >= SVR_VideoLocal.MEDIA_VERSION)
        {
            return;
        }

        var place = obj.FirstResolvedPlace;
        if (place != null) Utils.ServiceContainer.GetRequiredService<VideoLocal_PlaceService>().RefreshMediaInfo(place);
    }

    public override void Delete(SVR_VideoLocal obj)
    {
        var list = obj.AnimeEpisodes;
        base.Delete(obj);
        list.Where(a => a != null).ForEach(a => RepoFactory.AnimeEpisode.Save(a));
    }

    public override void Save(SVR_VideoLocal obj)
    {
        Save(obj, true);
    }

    public void Save(SVR_VideoLocal obj, bool updateEpisodes)
    {
        if (obj.VideoLocalID == 0)
        {
            obj.MediaInfo = null;
            base.Save(obj);
        }

        UpdateMediaContracts(obj);
        base.Save(obj);

        if (updateEpisodes)
        {
            RepoFactory.AnimeEpisode.Save(obj.AnimeEpisodes);
        }
    }

    public SVR_VideoLocal GetByHash(string hash)
    {
        if (string.IsNullOrEmpty(hash)) throw new InvalidStateException("Trying to lookup a VideoLocal by an empty Hash");
        return ReadLock(() => _hashes.GetOne(hash));
    }

    public SVR_VideoLocal GetByHashAndSize(string hash, long fileSize)
    {
        if (string.IsNullOrEmpty(hash)) throw new InvalidStateException("Trying to lookup a VideoLocal by an empty Hash");
        if (fileSize <= 0) throw new InvalidStateException("Trying to lookup a VideoLocal by a filesize of 0");
        return ReadLock(() => _hashes.GetMultiple(hash).FirstOrDefault(a => a.FileSize == fileSize));
    }

    public SVR_VideoLocal GetByMD5(string hash)
    {
        if (string.IsNullOrEmpty(hash)) throw new InvalidStateException("Trying to lookup a VideoLocal by an empty MD5");
        return ReadLock(() => _md5.GetOne(hash));
    }
    public SVR_VideoLocal GetByMD5AndSize(string hash, long fileSize)
    {
        if (string.IsNullOrEmpty(hash)) throw new InvalidStateException("Trying to lookup a VideoLocal by an empty MD5");
        if (fileSize <= 0) throw new InvalidStateException("Trying to lookup a VideoLocal by a filesize of 0");
        return ReadLock(() => _md5.GetMultiple(hash).FirstOrDefault(a => a.FileSize == fileSize));
    }

    public SVR_VideoLocal GetBySHA1(string hash)
    {
        if (string.IsNullOrEmpty(hash)) throw new InvalidStateException("Trying to lookup a VideoLocal by an empty SHA1");
        return ReadLock(() => _sha1.GetOne(hash));
    }
    public SVR_VideoLocal GetBySHA1AndSize(string hash, long fileSize)
    {
        if (string.IsNullOrEmpty(hash)) throw new InvalidStateException("Trying to lookup a VideoLocal by an empty SHA1");
        if (fileSize <= 0) throw new InvalidStateException("Trying to lookup a VideoLocal by a filesize of 0");
        return ReadLock(() => _sha1.GetMultiple(hash).FirstOrDefault(a => a.FileSize == fileSize));
    }

    public SVR_VideoLocal GetByCRC32(string hash)
    {
        if (string.IsNullOrEmpty(hash)) throw new InvalidStateException("Trying to lookup a VideoLocal by an empty CRC32");
        return ReadLock(() => _crc32.GetOne(hash));
    }

    public SVR_VideoLocal GetByCRC32AndSize(string hash, long fileSize)
    {
        if (string.IsNullOrEmpty(hash)) throw new InvalidStateException("Trying to lookup a VideoLocal by an empty CRC32");
        if (fileSize <= 0) throw new InvalidStateException("Trying to lookup a VideoLocal by a filesize of 0");
        return ReadLock(() => _crc32.GetMultiple(hash).FirstOrDefault(a => a.FileSize == fileSize));
    }

    public List<SVR_VideoLocal> GetByName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) throw new InvalidStateException("Trying to lookup a VideoLocal by an empty Filename");
        return ReadLock(
            () => Cache.Values.Where(
                    p => p.Places.Any(
                        a => a.FilePath.FuzzyMatch(fileName)
                    )
                )
                .ToList()
        );
    }

    public List<SVR_VideoLocal> GetMostRecentlyAdded(int maxResults, int jmmuserID)
    {
        var user = RepoFactory.JMMUser.GetByID(jmmuserID);
        if (user == null)
        {
            return ReadLock(() =>
                maxResults == -1
                    ? Cache.Values.OrderByDescending(a => a.DateTimeCreated).ToList()
                    : Cache.Values.OrderByDescending(a => a.DateTimeCreated).Take(maxResults).ToList());
        }

        if (maxResults == -1)
        {
            return ReadLock(
                () => Cache.Values
                    .Where(
                        a => a.AnimeEpisodes.Select(b => b.AnimeSeries).Where(b => b != null)
                            .DistinctBy(b => b.AniDB_ID).All(user.AllowedSeries)
                    ).OrderByDescending(a => a.DateTimeCreated)
                    .ToList()
            );
        }

        return ReadLock(
            () => Cache.Values
                .Where(a => a.AnimeEpisodes.Select(b => b.AnimeSeries).Where(b => b != null)
                    .DistinctBy(b => b.AniDB_ID).All(user.AllowedSeries)).OrderByDescending(a => a.DateTimeCreated)
                .Take(maxResults).ToList()
        );
    }

    public List<SVR_VideoLocal> GetMostRecentlyAdded(int take, int skip, int jmmuserID)
    {
        if (skip < 0)
        {
            skip = 0;
        }

        if (take == 0)
        {
            return new List<SVR_VideoLocal>();
        }

        var user = jmmuserID == -1 ? null : RepoFactory.JMMUser.GetByID(jmmuserID);
        if (user == null)
        {
            return ReadLock(() =>
                take == -1
                    ? Cache.Values.OrderByDescending(a => a.DateTimeCreated).Skip(skip).ToList()
                    : Cache.Values.OrderByDescending(a => a.DateTimeCreated).Skip(skip).Take(take).ToList());
        }

        return ReadLock(
            () => take == -1
                ? Cache.Values
                    .Where(a => a.AnimeEpisodes.Select(b => b.AnimeSeries).Where(b => b != null)
                        .DistinctBy(b => b.AniDB_ID).All(user.AllowedSeries))
                    .OrderByDescending(a => a.DateTimeCreated)
                    .Skip(skip)
                    .ToList()
                : Cache.Values
                    .Where(a => a.AnimeEpisodes.Select(b => b.AnimeSeries).Where(b => b != null)
                        .DistinctBy(b => b.AniDB_ID).All(user.AllowedSeries))
                    .OrderByDescending(a => a.DateTimeCreated)
                    .Skip(skip)
                    .Take(take)
                    .ToList()
        );
    }

    public List<SVR_VideoLocal> GetRandomFiles(int maxResults)
    {
        var values = ReadLock(Cache.Values.ToList).Where(a => a.EpisodeCrossRefs.Any()).ToList();

        using var en = new UniqueRandoms(0, values.Count - 1).GetEnumerator();
        var vids = new List<SVR_VideoLocal>();
        if (maxResults > values.Count)
        {
            maxResults = values.Count;
        }

        for (var x = 0; x < maxResults; x++)
        {
            en.MoveNext();
            vids.Add(values.ElementAt(en.Current));
        }

        return vids;
    }

    public class UniqueRandoms : IEnumerable<int>
    {
        private readonly Random _rand = new();
        private readonly List<int> _candidates;

        public UniqueRandoms(int minInclusive, int maxInclusive)
        {
            _candidates =
                Enumerable.Range(minInclusive, maxInclusive - minInclusive + 1).ToList();
        }

        public IEnumerator<int> GetEnumerator()
        {
            while (_candidates.Count > 0)
            {
                var index = _rand.Next(_candidates.Count);
                yield return _candidates[index];
                _candidates.RemoveAt(index);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }


    /// <summary>
    /// returns all the VideoLocal records associate with an AnimeEpisode Record
    /// </summary>
    /// <param name="episodeID">AniDB Episode ID</param>
    /// <returns></returns>
    /// 
    public List<SVR_VideoLocal> GetByAniDBEpisodeID(int episodeID)
    {
        return RepoFactory.CrossRef_File_Episode.GetByEpisodeID(episodeID)
            .Select(a => GetByHash(a.Hash))
            .Where(a => a != null)
            .ToList();
    }


    public List<SVR_VideoLocal> GetMostRecentlyAddedForAnime(int maxResults, int animeID)
    {
        return
            RepoFactory.CrossRef_File_Episode.GetByAnimeID(animeID)
                .Select(a => GetByHash(a.Hash))
                .Where(a => a != null)
                .OrderByDescending(a => a.DateTimeCreated)
                .Take(maxResults)
                .ToList();
    }

    public List<SVR_VideoLocal> GetByInternalVersion(int iver)
    {
        return RepoFactory.AniDB_File.GetByInternalVersion(iver)
            .Select(a => GetByHash(a.Hash))
            .Where(a => a != null)
            .ToList();
    }

    /// <summary>
    /// returns all the VideoLocal records associate with an AniDB_Anime Record
    /// </summary>
    /// <param name="animeID">AniDB Anime ID</param>
    /// <param name="xrefSource">Include to select only files from the selected
    /// cross-reference source.</param>
    /// <returns></returns>
    public List<SVR_VideoLocal> GetByAniDBAnimeID(int animeID, CrossRefSource? xrefSource = null)
    {
        if (xrefSource.HasValue)
            return
                RepoFactory.CrossRef_File_Episode.GetByAnimeID(animeID)
                    .Where(xref => xref.CrossRefSource == (int)xrefSource.Value)
                    .Select(xref => GetByHash(xref.Hash))
                    .WhereNotNull()
                    .ToList();

        return
            RepoFactory.CrossRef_File_Episode.GetByAnimeID(animeID)
                .Select(a => GetByHash(a.Hash))
                .WhereNotNull()
                .ToList();
    }

    public List<SVR_VideoLocal> GetVideosWithoutHash()
    {
        return ReadLock(() => _hashes.GetMultiple(""));
    }

    public List<SVR_VideoLocal> GetVideosWithoutEpisode(bool includeBrokenXRefs = false)
    {
        return ReadLock(
            () => Cache.Values
                .Where(a =>
                {
                    if (a.IsIgnored)
                        return false;

                    var xrefs = RepoFactory.CrossRef_File_Episode.GetByHash(a.Hash);
                    if (!xrefs.Any())
                        return true;

                    if (includeBrokenXRefs)
                        return !xrefs.Any(IsImported);

                    return false;
                })
                .OrderByNatural(local =>
                {
                    var place = local?.FirstValidPlace;
                    if (place == null) return null;
                    return place.FullServerPath ?? place.FilePath;
                })
                .ThenBy(local => local?.VideoLocalID ?? 0)
                .ToList()
        );
    }

    public List<SVR_VideoLocal> GetVideosWithMissingCrossReferenceData()
    {
        return ReadLock(
            () => Cache.Values
                .Where(a =>
                {
                    if (a.IsIgnored)
                        return false;

                    var xrefs = RepoFactory.CrossRef_File_Episode.GetByHash(a.Hash);
                    if (!xrefs.Any())
                        return false;

                    return !xrefs.All(IsImported);
                })
                .OrderByNatural(local =>
                {
                    var place = local?.FirstValidPlace;
                    if (place == null) return null;
                    return place.FullServerPath ?? place.FilePath;
                })
                .ThenBy(local => local?.VideoLocalID ?? 0)
                .ToList()
        );
    }

    private static bool IsImported(SVR_CrossRef_File_Episode xref)
    {
        if (xref.AnimeID == 0) return false;
        var ep = RepoFactory.AnimeEpisode.GetByAniDBEpisodeID(xref.EpisodeID);
        if (ep?.AniDB_Episode == null) return false;
        var anime = RepoFactory.AnimeSeries.GetByAnimeID(xref.AnimeID);
        return anime?.AniDB_Anime != null;
    }

    public List<SVR_VideoLocal> GetVideosWithoutEpisodeUnsorted()
    {
        return ReadLock(() =>
            Cache.Values.Where(a => !a.IsIgnored && !RepoFactory.CrossRef_File_Episode.GetByHash(a.Hash).Any())
                .ToList());
    }

    public List<SVR_VideoLocal> GetManuallyLinkedVideos()
    {
        return
            RepoFactory.CrossRef_File_Episode.GetAll()
                .Where(a => a.CrossRefSource != 1)
                .Select(a => GetByHash(a.Hash))
                .Where(a => a != null)
                .ToList();
    }

    public List<SVR_VideoLocal> GetExactDuplicateVideos()
    {
        return
            RepoFactory.VideoLocalPlace.GetAll()
                .GroupBy(a => a.VideoLocalID)
                .Select(a => a.ToArray())
                .Where(a => a.Length > 1)
                .Select(a => GetByID(a[0].VideoLocalID))
                .Where(a => a != null)
                .ToList();
    }

    public List<SVR_VideoLocal> GetIgnoredVideos()
    {
        return ReadLock(() => _ignored.GetMultiple(true));
    }

    public SVR_VideoLocal GetByMyListID(int myListID)
    {
        return ReadLock(() => Cache.Values.FirstOrDefault(a => a.MyListID == myListID));
    }
}
