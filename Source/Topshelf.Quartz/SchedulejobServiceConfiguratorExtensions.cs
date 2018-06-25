using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Quartz;
using Quartz.Impl;
using Quartz.Spi;
using Topshelf.Logging;
using Topshelf.ServiceConfigurators;

namespace Topshelf.Quartz
{
	public static class ScheduleJobServiceConfiguratorExtensions
	{

	    private static readonly Func<IScheduler> DefaultSchedulerFactory = () => new StdSchedulerFactory().GetScheduler().Result;

        private static Func<IScheduler> _customSchedulerFactory;
		private static IScheduler Scheduler;
		internal static IJobFactory JobFactory;

		public static Func<IScheduler> SchedulerFactory
		{
			get { return _customSchedulerFactory ?? DefaultSchedulerFactory; }
			set { _customSchedulerFactory = value; }
		}

		private static IScheduler GetScheduler()
		{
			var scheduler = SchedulerFactory();
			
			if(JobFactory != null)
				scheduler.JobFactory = JobFactory;

			return scheduler;
		}

		public static ServiceConfigurator<T> UsingQuartzJobFactory<T, TJobFactory>(this ServiceConfigurator<T> configurator, Func<TJobFactory> jobFactory)
			where T : class
			where TJobFactory : IJobFactory
		{
			JobFactory = jobFactory();
			return configurator;
		}

		public static ServiceConfigurator<T> UsingQuartzJobFactory<T,TJobFactory>(this ServiceConfigurator<T> configurator) where T : class where TJobFactory : IJobFactory, new()
		{
			return UsingQuartzJobFactory(configurator, () => new TJobFactory());
		}

		public static ServiceConfigurator<T> ScheduleQuartzJob<T>(this ServiceConfigurator<T> configurator, Action<QuartzConfigurator> jobConfigurator, bool replaceJob = false) where T : class
		{
			ConfigureJob<T>(configurator, jobConfigurator, replaceJob);
			return configurator;
		}

		private static void ConfigureJob<T>(ServiceConfigurator<T> configurator, Action<QuartzConfigurator> jobConfigurator, bool replaceJob = false) where T : class
		{
			var log = HostLogger.Get(typeof(ScheduleJobServiceConfiguratorExtensions));

			var jobConfig = new QuartzConfigurator();
			jobConfigurator(jobConfig);

			if (jobConfig.JobEnabled == null || jobConfig.JobEnabled() || (jobConfig.Job == null || jobConfig.Triggers == null))
			{
				var jobDetail = jobConfig.Job();
				var jobTriggers = jobConfig.Triggers.Select(triggerFactory => triggerFactory()).Where(trigger => trigger != null);
                var jobListeners = jobConfig.JobListeners;
                var triggerListeners = jobConfig.TriggerListeners;
                var scheduleListeners = jobConfig.ScheduleListeners;
				configurator.BeforeStartingService(() =>
					                                   {
														   log.Debug("[Topshelf.Quartz] Scheduler starting up...");

					                                       if (Scheduler == null)
					                                       {
					                                           Scheduler = GetScheduler();
					                                       }


					                                       if (Scheduler == null || jobDetail == null || !jobTriggers.Any())
					                                       {
					                                           return;
					                                       }

					                                       var triggersForJob = new HashSet<ITrigger>(jobTriggers);
					                                       Scheduler.ScheduleJob(jobDetail, triggersForJob, replaceJob);
					                                       log.Info($"[Topshelf.Quartz] Scheduled Job: {jobDetail.Key}");
					
					                                       foreach(var trigger in triggersForJob)
					                                           log.Info($"[Topshelf.Quartz] Job Schedule: {trigger} - Next Fire Time (local): {(trigger.GetNextFireTimeUtc().HasValue ? trigger.GetNextFireTimeUtc().Value.ToLocalTime().ToString() : "none")}");

					                                       if (jobListeners.Any())
					                                       {
					                                           foreach (var listener in jobListeners)
					                                           {
					                                               var config = listener();
                                                                   Scheduler.ListenerManager.AddJobListener(config.Listener, new ReadOnlyCollection<IMatcher<JobKey>>(config.Matchers));
					                                               log.Info($"[Topshelf.Quartz] Added Job Listener: {config.Listener.Name}");
					                                           }
					                                       }

					                                       if (triggerListeners.Any())
					                                       {
					                                           foreach (var listener in triggerListeners)
					                                           {
					                                               var config = listener();
					                                               Scheduler.ListenerManager.AddTriggerListener(config.Listener, new ReadOnlyCollection<IMatcher<TriggerKey>>(config.Matchers));
                                                                   
					                                               log.Info($"[Topshelf.Quartz] Added Trigger Listener: {config.Listener.Name}");
					                                           }
					                                       }
					                                       if (scheduleListeners.Any())
					                                       {
					                                           foreach (var listener in scheduleListeners)
					                                           {
					                                               var schedListener = listener();
					                                               Scheduler.ListenerManager.AddSchedulerListener(schedListener);
                                                                   log.Info($"[Topshelf.Quartz] Added Schedule Listener: {schedListener.GetType()}");
					                                           }

					                                       }

					                                       Scheduler.Start();
					                                       log.Info("[Topshelf.Quartz] Scheduler started...");

					                                   });

				configurator.BeforeStoppingService(() =>
						                {
											log.Debug("[Topshelf.Quartz] Scheduler shutting down...");
											if(Scheduler != null)
                                                if(!Scheduler.IsShutdown)
												    Scheduler.Shutdown();
											log.Info("[Topshelf.Quartz] Scheduler shut down...");
						                });

			}
		}
	}
}
