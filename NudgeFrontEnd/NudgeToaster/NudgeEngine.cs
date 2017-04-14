﻿using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.Background;
using Windows.ApplicationModel.Resources;
using Windows.UI.Notifications;
using Windows.UI.Popups;
using BackgroundTasks;
using NotificationsExtensions;
using NotificationsExtensions.Toasts;

namespace NudgeToaster
{
    class NudgeEngine
    {
        public NudgeEngine(Action<String> output)
        {
            this.output = output;
        }

        private Timer engineTimer;
        private ToastContent nudgeToaster;
        public Action<string> output;
        private const int cycle = 1000 * 60;



        private void NudgeEngineTimerCallback(object state)
        {
            nudge();
        }

        public void startEngine()
        {
            engineTimer = new Timer(NudgeEngineTimerCallback, null, 0, cycle);
        }

        public async void nudge()
        {
            // Clear all existing notifications
            ToastNotificationManager.History.Clear();
            // Register background task
            if (!await RegisterBackgroundTask())
            {
                await new MessageDialog("ERROR: Couldn't register background task.").ShowAsync();
                return;
            }
            buildNotif();
            Show(nudgeToaster);
        }

        private void buildNotif()
        {

            String time = DateTime.Now.ToString("HH:mm tt");
            nudgeToaster = new ToastContent
            {
                ActivationType = ToastActivationType.Background,
                Visual = new ToastVisual
                {
                    BindingGeneric = new ToastBindingGeneric()
                    {
                        Children =
                        {
                            new AdaptiveText()
                            {
                                Text = "It's currently " + time,
                                HintStyle = AdaptiveTextStyle.Header
                            },
                            new AdaptiveText()
                            {
                                Text = "Is this really what you want to be doing right now? ",
                                HintStyle = AdaptiveTextStyle.Body,
                                HintWrap = true
                            },
                            new AdaptiveImage()
                            {
                                Source = "Assets/Nudge.png"
                            }

                        }
                    }
                },
                Launch = "394815",
                Scenario = ToastScenario.Default,
                Actions = new ToastActionsCustom
                {
                    Buttons =
                    {
                        new ToastButton("Yes", "Yes" )
                        {
                            ActivationType = ToastActivationType.Background
                        },
                        new ToastButton("No", "No")
                        {
                            ActivationType = ToastActivationType.Background
                        }
                    }
                }
            };
        }

        private static void Show(ToastContent content)
        {
            ToastNotificationManager.CreateToastNotifier().Show(new ToastNotification(content.GetXml()));
        }

        private static string BACKGROUND_ENTRY_POINT = typeof(NotificationActionBackgroundTask).FullName;
        private BackgroundTaskRegistration registration;

        public async Task<bool> RegisterBackgroundTask()
        {
            // Unregister any previous exising background task
            UnregisterBackgroundTask();

            // Request access
            BackgroundAccessStatus status = await BackgroundExecutionManager.RequestAccessAsync();

            // If denied
            if (status != BackgroundAccessStatus.AlwaysAllowed && status != BackgroundAccessStatus.AllowedSubjectToSystemPolicy)
                return false;

            // Construct the background task
            BackgroundTaskBuilder builder = new BackgroundTaskBuilder()
            {
                Name = BACKGROUND_ENTRY_POINT,
                TaskEntryPoint = BACKGROUND_ENTRY_POINT
            };

            // Set trigger for Toast History Changed
            builder.SetTrigger(new ToastNotificationActionTrigger());


            // And register the background task
            registration = builder.Register();
            registration.Progress += OnProgress;
            registration.Completed+= RegistrationOnCompleted;
            return true;
        }

        private void RegistrationOnCompleted(BackgroundTaskRegistration sender, BackgroundTaskCompletedEventArgs args)
        {
            output("Completed");
        }

        private void OnProgress(IBackgroundTaskRegistration task, BackgroundTaskProgressEventArgs args)
        {
            var progress = "Progress: " + args.Progress + "%";
            output(progress);
        }

        private static void UnregisterBackgroundTask()
        {
            var task = BackgroundTaskRegistration.AllTasks.Values.FirstOrDefault(i => i.Name.Equals(BACKGROUND_ENTRY_POINT));
            task?.Unregister(true);
        }


    }


}