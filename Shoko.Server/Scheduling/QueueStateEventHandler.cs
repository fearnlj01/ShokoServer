using System;
using System.Collections.Generic;
using System.Linq;
using Quartz;

namespace Shoko.Server.Scheduling;

public class QueueStateEventHandler
{
    private readonly JobFactory _jobFactory;
    private bool _isPaused;
    private bool _isRunning;

    public event EventHandler QueuePaused;
    public event EventHandler QueueStarted;
    public event EventHandler<QueueChangedEventArgs> QueueChanged;

    public void InvokeQueuePaused()
    {
        if (_isPaused) return;
        _isRunning = false;
        _isPaused = true;
        QueuePaused?.Invoke(null, EventArgs.Empty);
    }

    public void InvokeQueueStarted()
    {
        if (_isRunning) return;
        _isPaused = false;
        _isRunning = true;
        QueueStarted?.Invoke(null, EventArgs.Empty);
    }

    public void OnJobExecuting(IJobDetail jobDetail, QueueStateContext queueContext)
    {
        var job = _jobFactory.CreateJob(jobDetail);

        QueueChanged?.Invoke(null, new QueueChangedEventArgs
        {
            AddedItems = new List<QueueItem>
            {
                new()
                {
                    Key = jobDetail.Key.ToString(), JobType = job?.Name ?? jobDetail.JobType.Name, Description = job?.Description.formatMessage()
                }
            },
            WaitingJobsCount = queueContext.WaitingTriggersCount,
            BlockedJobsCount = queueContext.BlockedTriggersCount,
            ThreadCount = queueContext.ThreadCount,
            ExecutingItems = queueContext.CurrentlyExecuting.ToList()
        });
    }

    public void OnJobCompleted(IJobDetail jobDetail, QueueStateContext queueContext)
    {
        var job = _jobFactory.CreateJob(jobDetail);

        QueueChanged?.Invoke(null, new QueueChangedEventArgs
        {
            RemovedItems = new List<QueueItem>
            {
                new()
                {
                    Key = jobDetail.Key.ToString(), JobType = job?.Name ?? jobDetail.JobType.Name, Description = job?.Description.formatMessage()
                }
            },
            WaitingJobsCount = queueContext.WaitingTriggersCount,
            BlockedJobsCount = queueContext.BlockedTriggersCount,
            ThreadCount = queueContext.ThreadCount,
            ExecutingItems = queueContext.CurrentlyExecuting.ToList()
        });
    }

    public QueueStateEventHandler(JobFactory jobFactory)
    {
        _jobFactory = jobFactory;
    }
}