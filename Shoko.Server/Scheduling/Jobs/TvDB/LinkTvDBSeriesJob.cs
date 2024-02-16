using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using QuartzJobFactory.Attributes;
using Shoko.Commons.Queue;
using Shoko.Models.Queue;
using Shoko.Server.Models;
using Shoko.Server.Providers.TvDB;
using Shoko.Server.Scheduling.Acquisition.Attributes;
using Shoko.Server.Scheduling.Concurrency;

namespace Shoko.Server.Scheduling.Jobs.TvDB;

[DatabaseRequired]
[NetworkRequired]
[DisallowConcurrencyGroup(ConcurrencyGroups.TvDB)]
[JobKeyGroup(JobKeyGroup.TvDB)]
public class LinkTvDBSeriesJob : BaseJob
{
    private readonly TvDBApiHelper _helper;
    public int AnimeID { get; set; }
    public int TvDBID { get; set; }
    public bool AdditiveLink { get; set; }

    public override string Name => "Link TvDB Series";

    public override QueueStateStruct Description => new()
    {
        message = "Linking TvDB: {0} to AniDB: {1}",
        queueState = QueueStateEnum.LinkAniDBTvDB,
        extraParams = new[] { TvDBID.ToString(), AnimeID.ToString() }
    };

    public override async Task Process()
    {
        _logger.LogInformation("Processing {Job} -> TvDB: {TvDB} | AniDB: {AniDB} | Additive: {Additive}", nameof(LinkTvDBSeriesJob), TvDBID, AnimeID,
            AdditiveLink);

        await _helper.LinkAniDBTvDB(AnimeID, TvDBID, AdditiveLink);
        SVR_AniDB_Anime.UpdateStatsByAnimeID(AnimeID);
    }

    public LinkTvDBSeriesJob(TvDBApiHelper helper)
    {
        _helper = helper;
    }

    protected LinkTvDBSeriesJob() { }
}