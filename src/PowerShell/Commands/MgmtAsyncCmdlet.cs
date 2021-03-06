﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Online.SecMgmt.PowerShell.Commands
{
    using System;
    using System.Collections.Concurrent;
    using System.Management.Automation;
    using System.Threading.Tasks;
    using Models;
    using Models.Authentication;
    using Utilities;

    /// <summary>
    /// The base class for asynchronous cmdlets.
    /// </summary>
    public abstract class MgmtAsyncCmdlet : MgmtPSCmdlet
    {
        /// <summary>
        /// Key for the write debug component.
        /// </summary>
        private const string WriteDebugKey = "WriteDebug";

        /// <summary>
        /// Key for the write error component.
        /// </summary>
        private const string WriteErrorKey = "WriteError";

        /// <summary>
        /// Key for the write object component.
        /// </summary>
        private const string WriteObjectKey = "WriteObject";

        /// <summary>
        /// Key for the write warning component.
        /// </summary>
        private const string WriteWarningKey = "WriteWarning";

        /// <summary>
        /// The event that is triggered when the write debug operation is invoked.
        /// </summary>
        private event EventHandler<StreamEventArgs> OnWriteDebug;

        /// <summary>
        /// The event that is triggered when the write error operation is invoked.
        /// </summary>
        private event EventHandler<StreamEventArgs> OnWriteError;

        /// <summary>
        /// The event that is triggered when the write object operation is invoked.
        /// </summary>
        private event EventHandler<StreamEventArgs> OnWriteObject;

        /// <summary>
        /// The event that is triggered when the write warning operation is invoked.
        /// </summary>
        private event EventHandler<StreamEventArgs> OnWriteWarning;

        /// <summary>
        /// The queue of output tasks to be invoked.
        /// </summary>
        private readonly ConcurrentQueue<Task> outputTasks = new ConcurrentQueue<Task>();

        /// <summary>
        /// The event that is triggered when the task is completed.k
        /// </summary>
        public event EventHandler OnTaskCompleted;

        /// <summary>
        /// Gets the scheduler used for task execution.
        /// </summary>
        public ConcurrencyTaskScheduler Scheduler { get; private set; }

        /// <summary>
        /// Operations that happen before the cmdlet is invoked.
        /// </summary>
        protected override void BeginProcessing()
        {
            base.BeginProcessing();

            Scheduler = new ConcurrencyTaskScheduler(100, CancellationToken);

            Scheduler.OnError += Scheduler_OnError;

            OnWriteDebug -= Cmdlet_OnWriteDebug;
            OnWriteDebug += Cmdlet_OnWriteDebug;

            OnWriteError -= Cmdlet_OnWriteError;
            OnWriteError += Cmdlet_OnWriteError;

            OnWriteObject -= Cmdlet_OnWriteObject;
            OnWriteObject += Cmdlet_OnWriteObject;

            OnWriteWarning -= Cmdlet_OnWriteWarning;
            OnWriteWarning += Cmdlet_OnWriteWarning;

            MgmtSession.Instance.UnregisterComponent(WriteDebugKey);
            MgmtSession.Instance.UnregisterComponent(WriteErrorKey);
            MgmtSession.Instance.UnregisterComponent(WriteObjectKey);
            MgmtSession.Instance.UnregisterComponent(WriteWarningKey);

            MgmtSession.Instance.RegisterComponent(WriteDebugKey, () => OnWriteDebug);
            MgmtSession.Instance.RegisterComponent(WriteErrorKey, () => OnWriteError);
            MgmtSession.Instance.RegisterComponent(WriteObjectKey, () => OnWriteObject);
            MgmtSession.Instance.RegisterComponent(WriteWarningKey, () => OnWriteWarning);
        }

        /// <summary>
        /// Operations that happend after the cmdlet is invoked.
        /// </summary>
        protected override void EndProcessing()
        {
            try
            {
                do
                {
                    while (outputTasks.TryDequeue(out Task task))
                    {
                        task?.RunSynchronously();
                    }
                }
                while (!Scheduler.CheckForComplete(500, CancellationToken));

                OnTaskCompleted?.Invoke(this, EventArgs.Empty);

                if (!outputTasks.IsEmpty)
                {
                    while (outputTasks.TryDequeue(out Task task))
                    {
                        task?.RunSynchronously();
                    }
                }
            }
            finally
            {
                Scheduler?.Dispose();
            }

            base.EndProcessing();
        }

        /// <summary>
        /// Writes the debug information.
        /// </summary>
        /// <param name="text">The text to be written to the pipeline.</param>
        protected new void WriteDebug(string text)
        {
            if (MgmtSession.Instance.TryGetComponent(WriteDebugKey, out EventHandler<StreamEventArgs> writeObjectEvent))
            {
                writeObjectEvent(this, new StreamEventArgs { Resource = text });
            }
        }

        /// <summary>
        /// Writes the error record to the pipeline.
        /// </summary>
        /// <param name="errorRecord">The error record to be written to the pipeline.</param>
        protected new void WriteError(ErrorRecord errorRecord)
        {
            if (MgmtSession.Instance.TryGetComponent(WriteDebugKey, out EventHandler<StreamEventArgs> writeObjectEvent))
            {
                writeObjectEvent(this, new StreamEventArgs { Resource = errorRecord });
            }
        }

        /// <summary>
        /// Writes the object the pipeline.
        /// </summary>
        /// <param name="sendToPipeline">The object to be written to the pipeline.</param>
        protected new void WriteObject(object sendToPipeline)
        {
            if (MgmtSession.Instance.TryGetComponent(WriteObjectKey, out EventHandler<StreamEventArgs> writeObjectEvent))
            {
                writeObjectEvent(this, new StreamEventArgs { EnumerateCollection = false, Resource = sendToPipeline });
            }
        }

        /// <summary>
        /// Writes the object the pipeline.
        /// </summary>
        /// <param name="sendToPipeline">The object to be written to the pipeline.</param>
        /// <param name="enumerateCollection">A flag indicating whether or not to enumerate the collection.</param>
        protected new void WriteObject(object sendToPipeline, bool enumerateCollection)
        {
            if (MgmtSession.Instance.TryGetComponent(WriteObjectKey, out EventHandler<StreamEventArgs> writeObjectEvent))
            {
                writeObjectEvent(this, new StreamEventArgs { EnumerateCollection = enumerateCollection, Resource = sendToPipeline });
            }
        }

        /// <summary>
        /// Writes the warning message to the pipeline.
        /// </summary>
        /// <param name="text">The text to be written to the pipeline.</param>
        protected new void WriteWarning(string text)
        {
            if (MgmtSession.Instance.TryGetComponent(WriteWarningKey, out EventHandler<StreamEventArgs> writeWarningEvent))
            {
                writeWarningEvent(this, new StreamEventArgs() { Resource = text });
            }
        }

        private void Cmdlet_OnWriteDebug(object sender, StreamEventArgs args)
        {
            outputTasks.Enqueue(new Task(() => base.WriteDebug(args.Resource.ToString())));
        }

        private void Cmdlet_OnWriteError(object sender, StreamEventArgs e)
        {
            outputTasks.Enqueue(new Task(() => base.WriteExceptionError(e.Resource as Exception)));
        }

        private void Cmdlet_OnWriteObject(object sender, StreamEventArgs args)
        {
            outputTasks.Enqueue(new Task(() => base.WriteObject(args.Resource, args.EnumerateCollection)));
        }

        private void Cmdlet_OnWriteWarning(object sender, StreamEventArgs args)
        {
            outputTasks.Enqueue(new Task(() => base.WriteWarning(args.Resource.ToString())));
        }

        private void Scheduler_OnError(object sender, TaskExceptionEventArgs e)
        {
            if (MgmtSession.Instance.TryGetComponent(WriteErrorKey, out EventHandler<StreamEventArgs> writeErrorEvent))
            {
                writeErrorEvent(this, new StreamEventArgs() { Resource = e.Exception });
            }
        }
    }
}