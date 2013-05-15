using System;
using System.Collections.Generic;
using System.Net;
using System.Security;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace TestApp.Helpers
{
    // TODO: make the test app run in full trust to be able to use custom task schedulers
    //public class FrozenTaskScheduler : TaskScheduler
    //{
    //    private Queue<Task> _queue = new Queue<Task>();
    //    private readonly object _queueLock = new object();

    //    [SecurityCritical]
    //    protected override IEnumerable<Task> GetScheduledTasks()
    //    {
    //        Task[] queueClone;
    //        lock (_queueLock)
    //        {
    //            queueClone = _queue.ToArray();
    //        }

    //        return queueClone;
    //    }

    //    [SecurityCritical]
    //    protected override void QueueTask(Task task)
    //    {
    //        lock (_queueLock)
    //        {
    //            _queue.Enqueue(task);
    //        }
    //    }

    //    [SecurityCritical]
    //    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
    //    {
    //        return false;
    //    }
    //}
}
